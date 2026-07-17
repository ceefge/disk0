namespace TeamsPoll.Server.Models;

/// <summary>A single poll with its question, options and settings.</summary>
public class Poll
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Question { get; set; } = string.Empty;

    /// <summary>Display name of the creator (for the card footer).</summary>
    public string CreatedByName { get; set; } = string.Empty;

    /// <summary>Stable Teams/AAD user id of the creator (used for permission checks).</summary>
    public string CreatedById { get; set; } = string.Empty;

    /// <summary>Conversation the poll card lives in.</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Id of the posted card activity (used for proactive updates / refresh).</summary>
    public string? ActivityId { get; set; }

    /// <summary>Allow voting for more than one option.</summary>
    public bool AllowMultiple { get; set; }

    /// <summary>Hide who voted for what; only show counts.</summary>
    public bool Anonymous { get; set; }

    public bool Closed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PollOption> Options { get; set; } = new();
}
