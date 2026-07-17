using Microsoft.Data.Sqlite;
using TeamsPoll.Server.Models;

namespace TeamsPoll.Server.Data;

/// <summary>
/// SQLite-backed storage for polls, options and votes.
///
/// A connection is opened per operation; SQLite serialises writers itself and
/// WAL + busy_timeout keeps concurrent votes from failing under normal load.
/// </summary>
public class PollRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PollRepository> _logger;

    public PollRepository(IConfiguration configuration, ILogger<PollRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Sqlite") ?? "Data Source=polls.db";
        _logger = logger;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            // WAL improves read/write concurrency; busy_timeout retries locked writes.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        return connection;
    }

    public async Task InitializeAsync()
    {
        await using var connection = OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Polls (
                Id            TEXT PRIMARY KEY,
                Question      TEXT NOT NULL,
                CreatedByName TEXT NOT NULL,
                CreatedById   TEXT NOT NULL,
                ConversationId TEXT NOT NULL,
                ActivityId    TEXT,
                AllowMultiple INTEGER NOT NULL DEFAULT 0,
                Anonymous     INTEGER NOT NULL DEFAULT 0,
                Closed        INTEGER NOT NULL DEFAULT 0,
                CreatedAt     TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Options (
                Id         TEXT PRIMARY KEY,
                PollId     TEXT NOT NULL REFERENCES Polls(Id) ON DELETE CASCADE,
                Text       TEXT NOT NULL,
                OrderIndex INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Votes (
                PollId    TEXT NOT NULL REFERENCES Polls(Id) ON DELETE CASCADE,
                OptionId  TEXT NOT NULL REFERENCES Options(Id) ON DELETE CASCADE,
                UserId    TEXT NOT NULL,
                UserName  TEXT NOT NULL,
                VotedAt   TEXT NOT NULL,
                PRIMARY KEY (OptionId, UserId)
            );

            CREATE INDEX IF NOT EXISTS IX_Options_Poll ON Options(PollId);
            CREATE INDEX IF NOT EXISTS IX_Votes_Poll   ON Votes(PollId);
            """;
        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("Poll database initialised at {ConnectionString}", _connectionString);
    }

    public async Task CreatePollAsync(Poll poll)
    {
        await using var connection = OpenConnection();
        await using var tx = connection.BeginTransaction();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Polls (Id, Question, CreatedByName, CreatedById, ConversationId,
                                   ActivityId, AllowMultiple, Anonymous, Closed, CreatedAt)
                VALUES ($id, $q, $byName, $byId, $conv, $activity, $multi, $anon, 0, $created);
                """;
            cmd.Parameters.AddWithValue("$id", poll.Id);
            cmd.Parameters.AddWithValue("$q", poll.Question);
            cmd.Parameters.AddWithValue("$byName", poll.CreatedByName);
            cmd.Parameters.AddWithValue("$byId", poll.CreatedById);
            cmd.Parameters.AddWithValue("$conv", poll.ConversationId);
            cmd.Parameters.AddWithValue("$activity", (object?)poll.ActivityId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$multi", poll.AllowMultiple ? 1 : 0);
            cmd.Parameters.AddWithValue("$anon", poll.Anonymous ? 1 : 0);
            cmd.Parameters.AddWithValue("$created", poll.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var option in poll.Options)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Options (Id, PollId, Text, OrderIndex)
                VALUES ($id, $pollId, $text, $order);
                """;
            cmd.Parameters.AddWithValue("$id", option.Id);
            cmd.Parameters.AddWithValue("$pollId", poll.Id);
            cmd.Parameters.AddWithValue("$text", option.Text);
            cmd.Parameters.AddWithValue("$order", option.OrderIndex);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<Poll?> GetPollAsync(string pollId)
    {
        await using var connection = OpenConnection();

        Poll? poll = null;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, Question, CreatedByName, CreatedById, ConversationId, ActivityId,
                       AllowMultiple, Anonymous, Closed, CreatedAt
                FROM Polls WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", pollId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                poll = new Poll
                {
                    Id = reader.GetString(0),
                    Question = reader.GetString(1),
                    CreatedByName = reader.GetString(2),
                    CreatedById = reader.GetString(3),
                    ConversationId = reader.GetString(4),
                    ActivityId = reader.IsDBNull(5) ? null : reader.GetString(5),
                    AllowMultiple = reader.GetInt32(6) == 1,
                    Anonymous = reader.GetInt32(7) == 1,
                    Closed = reader.GetInt32(8) == 1,
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
                };
            }
        }

        if (poll is null)
            return null;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT Id, PollId, Text, OrderIndex FROM Options
                WHERE PollId = $id ORDER BY OrderIndex;
                """;
            cmd.Parameters.AddWithValue("$id", pollId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                poll.Options.Add(new PollOption
                {
                    Id = reader.GetString(0),
                    PollId = reader.GetString(1),
                    Text = reader.GetString(2),
                    OrderIndex = reader.GetInt32(3),
                });
            }
        }

        return poll;
    }

    public async Task SetActivityIdAsync(string pollId, string activityId)
    {
        await using var connection = OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Polls SET ActivityId = $activity WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$activity", activityId);
        cmd.Parameters.AddWithValue("$id", pollId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Applies a vote toggle and returns the fresh results.
    /// Single-choice: tapping the current choice removes it; tapping another moves the vote.
    /// Multi-choice: each tap toggles that option independently.
    /// </summary>
    public async Task<PollResults?> ToggleVoteAsync(
        string pollId, string optionId, string userId, string userName)
    {
        var poll = await GetPollAsync(pollId);
        if (poll is null || poll.Closed)
            return poll is null ? null : await GetResultsAsync(pollId);

        // Guard against option ids that don't belong to this poll.
        if (poll.Options.All(o => o.Id != optionId))
            return await GetResultsAsync(pollId);

        await using var connection = OpenConnection();
        await using var tx = connection.BeginTransaction();

        bool alreadyVotedThis;
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM Votes WHERE OptionId = $opt AND UserId = $user;";
            check.Parameters.AddWithValue("$opt", optionId);
            check.Parameters.AddWithValue("$user", userId);
            alreadyVotedThis = Convert.ToInt64(await check.ExecuteScalarAsync()) > 0;
        }

        if (alreadyVotedThis)
        {
            // Toggle off.
            await using var del = connection.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Votes WHERE OptionId = $opt AND UserId = $user;";
            del.Parameters.AddWithValue("$opt", optionId);
            del.Parameters.AddWithValue("$user", userId);
            await del.ExecuteNonQueryAsync();
        }
        else
        {
            if (!poll.AllowMultiple)
            {
                // Single choice: clear any previous vote in this poll first.
                await using var clear = connection.CreateCommand();
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM Votes WHERE PollId = $poll AND UserId = $user;";
                clear.Parameters.AddWithValue("$poll", pollId);
                clear.Parameters.AddWithValue("$user", userId);
                await clear.ExecuteNonQueryAsync();
            }

            await using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO Votes (PollId, OptionId, UserId, UserName, VotedAt)
                VALUES ($poll, $opt, $user, $name, $at);
                """;
            ins.Parameters.AddWithValue("$poll", pollId);
            ins.Parameters.AddWithValue("$opt", optionId);
            ins.Parameters.AddWithValue("$user", userId);
            ins.Parameters.AddWithValue("$name", userName);
            ins.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("o"));
            await ins.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return await GetResultsAsync(pollId);
    }

    public async Task<PollResults> GetResultsAsync(string pollId)
    {
        await using var connection = OpenConnection();
        var results = new PollResults();

        // Options in order, so every option shows even with zero votes.
        var optionOrder = new List<(string Id, string Text, int Order)>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Text, OrderIndex FROM Options WHERE PollId = $id ORDER BY OrderIndex;";
            cmd.Parameters.AddWithValue("$id", pollId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                optionOrder.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        foreach (var (id, text, order) in optionOrder)
            results.Options.Add(new OptionResult { OptionId = id, Text = text, OrderIndex = order });

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT OptionId, UserId, UserName FROM Votes WHERE PollId = $id;";
            cmd.Parameters.AddWithValue("$id", pollId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var voters = new HashSet<string>();
            while (await reader.ReadAsync())
            {
                var optionId = reader.GetString(0);
                var userId = reader.GetString(1);
                var userName = reader.GetString(2);

                var option = results.Options.FirstOrDefault(o => o.OptionId == optionId);
                if (option is null)
                    continue;

                option.Count++;
                option.VoterNames.Add(userName);
                results.TotalVotes++;
                voters.Add(userId);
                if (!results.ParticipantIds.Contains(userId))
                    results.ParticipantIds.Add(userId);
            }
            results.TotalVoters = voters.Count;
        }

        return results;
    }

    /// <summary>Closes a poll. Only the creator may close it.</summary>
    public async Task<bool> ClosePollAsync(string pollId, string requestingUserId)
    {
        await using var connection = OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Polls SET Closed = 1 WHERE Id = $id AND CreatedById = $user;";
        cmd.Parameters.AddWithValue("$id", pollId);
        cmd.Parameters.AddWithValue("$user", requestingUserId);
        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }
}
