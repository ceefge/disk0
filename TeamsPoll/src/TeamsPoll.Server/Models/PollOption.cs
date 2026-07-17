namespace TeamsPoll.Server.Models;

/// <summary>One selectable answer of a poll.</summary>
public class PollOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string PollId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>Display order within the poll.</summary>
    public int OrderIndex { get; set; }
}
