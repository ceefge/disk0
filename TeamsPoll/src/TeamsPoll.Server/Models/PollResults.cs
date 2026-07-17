namespace TeamsPoll.Server.Models;

/// <summary>Aggregated results for one option.</summary>
public class OptionResult
{
    public string OptionId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public int Count { get; set; }

    /// <summary>Voter display names (empty when the poll is anonymous).</summary>
    public List<string> VoterNames { get; set; } = new();
}

/// <summary>Aggregated results across a whole poll.</summary>
public class PollResults
{
    public List<OptionResult> Options { get; set; } = new();

    /// <summary>Number of distinct users who cast at least one vote.</summary>
    public int TotalVoters { get; set; }

    /// <summary>Total number of votes (differs from TotalVoters for multi-select polls).</summary>
    public int TotalVotes { get; set; }

    /// <summary>Distinct user ids that interacted – used to drive card auto-refresh.</summary>
    public List<string> ParticipantIds { get; set; } = new();
}
