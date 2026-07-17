using System.Text.RegularExpressions;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using Newtonsoft.Json.Linq;
using TeamsPoll.Server.Cards;
using TeamsPoll.Server.Data;
using TeamsPoll.Server.Models;

namespace TeamsPoll.Server.Bot;

/// <summary>
/// The Teams poll bot. Handles two things:
///   1. Text messages — quick poll creation ("Frage? | A | B") or help.
///   2. Adaptive Card actions (Action.Execute) — voting, refresh, close and the
///      interactive create form.
/// </summary>
public class PollBot : TeamsActivityHandler
{
    private readonly PollRepository _repo;
    private readonly ILogger<PollBot> _logger;

    public PollBot(PollRepository repo, ILogger<PollBot> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    // --- Text messages ----------------------------------------------------

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
    {
        var text = (turnContext.Activity.RemoveRecipientMention() ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text) ||
            text.Equals("help", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("hilfe", StringComparison.OrdinalIgnoreCase))
        {
            await SendCardAsync(turnContext, PollCardFactory.BuildHelpCard(), cancellationToken);
            return;
        }

        var parsed = TryParsePollCommand(text);
        if (parsed is null)
        {
            await turnContext.SendActivityAsync(
                MessageFactory.Text("Das habe ich nicht als Umfrage erkannt. Format: `Frage? | Option 1 | Option 2`"),
                cancellationToken);
            await SendCardAsync(turnContext, PollCardFactory.BuildHelpCard(), cancellationToken);
            return;
        }

        var poll = BuildPoll(turnContext, parsed.Value.Question, parsed.Value.Options,
            parsed.Value.AllowMultiple, parsed.Value.Anonymous);
        await _repo.CreatePollAsync(poll);

        var results = await _repo.GetResultsAsync(poll.Id);
        var card = PollCardFactory.BuildPollCard(poll, results);

        var response = await SendCardAsync(turnContext, card, cancellationToken);
        if (response is not null)
            await _repo.SetActivityIdAsync(poll.Id, response.Id);
    }

    // --- Card actions (Action.Execute) ------------------------------------

    protected override async Task<AdaptiveCardInvokeResponse> OnAdaptiveCardInvokeAsync(
        ITurnContext<IInvokeActivity> turnContext,
        AdaptiveCardInvokeValue invokeValue,
        CancellationToken cancellationToken)
    {
        var verb = invokeValue.Action?.Verb ?? string.Empty;
        var data = invokeValue.Action?.Data as JObject
                   ?? JObject.FromObject(invokeValue.Action?.Data ?? new object());

        var userId = turnContext.Activity.From?.Id ?? "unknown";
        var userName = turnContext.Activity.From?.Name ?? "Unbekannt";

        switch (verb)
        {
            case PollCardFactory.VerbCreateForm:
                return CardResponse(PollCardFactory.BuildCreateForm());

            case PollCardFactory.VerbCreate:
                return await HandleCreateFromFormAsync(turnContext, data, cancellationToken);

            case PollCardFactory.VerbVote:
                return await HandleVoteAsync(data, userId, userName);

            case PollCardFactory.VerbRefresh:
                return await HandleRefreshAsync(data);

            case PollCardFactory.VerbClose:
                return await HandleCloseAsync(data, userId);

            default:
                _logger.LogWarning("Unknown card verb '{Verb}'", verb);
                return CardResponse(PollCardFactory.BuildHelpCard());
        }
    }

    private async Task<AdaptiveCardInvokeResponse> HandleCreateFromFormAsync(
        ITurnContext turnContext, JObject data, CancellationToken cancellationToken)
    {
        var question = (data.Value<string>("question") ?? string.Empty).Trim();
        var optionsRaw = data.Value<string>("options") ?? string.Empty;
        var allowMultiple = string.Equals(data.Value<string>("multi"), "true", StringComparison.OrdinalIgnoreCase);
        var anonymous = string.Equals(data.Value<string>("anon"), "true", StringComparison.OrdinalIgnoreCase);

        var options = optionsRaw
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim())
            .Where(o => o.Length > 0)
            .Distinct()
            .ToList();

        if (string.IsNullOrWhiteSpace(question) || options.Count < 2)
        {
            // Re-show the form; Teams keeps the entered values.
            return CardResponse(PollCardFactory.BuildCreateForm());
        }

        var poll = BuildPoll(turnContext, question, options, allowMultiple, anonymous);
        // The card being acted on becomes the poll card, so remember its id for refresh.
        poll.ActivityId = turnContext.Activity.ReplyToId;
        await _repo.CreatePollAsync(poll);

        var results = await _repo.GetResultsAsync(poll.Id);
        return CardResponse(PollCardFactory.BuildPollCard(poll, results));
    }

    private async Task<AdaptiveCardInvokeResponse> HandleVoteAsync(JObject data, string userId, string userName)
    {
        var pollId = data.Value<string>("pollId");
        var optionId = data.Value<string>("optionId");
        if (string.IsNullOrEmpty(pollId) || string.IsNullOrEmpty(optionId))
            return CardResponse(PollCardFactory.BuildHelpCard());

        var poll = await _repo.GetPollAsync(pollId);
        if (poll is null)
            return CardResponse(ClosedOrMissingCard());

        var results = await _repo.ToggleVoteAsync(pollId, optionId, userId, userName)
                      ?? await _repo.GetResultsAsync(pollId);

        return CardResponse(PollCardFactory.BuildPollCard(poll, results));
    }

    private async Task<AdaptiveCardInvokeResponse> HandleRefreshAsync(JObject data)
    {
        var pollId = data.Value<string>("pollId");
        if (string.IsNullOrEmpty(pollId))
            return CardResponse(PollCardFactory.BuildHelpCard());

        var poll = await _repo.GetPollAsync(pollId);
        if (poll is null)
            return CardResponse(ClosedOrMissingCard());

        var results = await _repo.GetResultsAsync(pollId);
        return CardResponse(PollCardFactory.BuildPollCard(poll, results));
    }

    private async Task<AdaptiveCardInvokeResponse> HandleCloseAsync(JObject data, string userId)
    {
        var pollId = data.Value<string>("pollId");
        if (string.IsNullOrEmpty(pollId))
            return CardResponse(PollCardFactory.BuildHelpCard());

        // Only the creator may close; otherwise nothing changes.
        await _repo.ClosePollAsync(pollId, userId);

        var poll = await _repo.GetPollAsync(pollId);
        if (poll is null)
            return CardResponse(ClosedOrMissingCard());

        var results = await _repo.GetResultsAsync(pollId);
        return CardResponse(PollCardFactory.BuildPollCard(poll, results));
    }

    // --- Welcome ----------------------------------------------------------
    // Two entry points: OnTeamsMembersAddedAsync fires inside Teams (the base
    // TeamsActivityHandler routes membersAdded there); OnMembersAddedAsync covers
    // non-Teams channels such as the Bot Framework Emulator.

    protected override Task OnTeamsMembersAddedAsync(
        IList<TeamsChannelAccount> membersAdded,
        TeamInfo teamInfo,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken cancellationToken)
        => MaybeWelcomeAsync(membersAdded, turnContext, cancellationToken);

    protected override Task OnMembersAddedAsync(
        IList<ChannelAccount> membersAdded,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken cancellationToken)
        => MaybeWelcomeAsync(membersAdded, turnContext, cancellationToken);

    /// <summary>Sends the help card once, when the bot itself is added.</summary>
    private async Task MaybeWelcomeAsync(
        IEnumerable<ChannelAccount> membersAdded,
        ITurnContext turnContext,
        CancellationToken cancellationToken)
    {
        var botId = turnContext.Activity.Recipient?.Id;
        if (membersAdded.Any(m => m.Id == botId))
            await SendCardAsync(turnContext, PollCardFactory.BuildHelpCard(), cancellationToken);
    }

    // --- Helpers ----------------------------------------------------------

    private static Poll BuildPoll(
        ITurnContext turnContext, string question, IReadOnlyList<string> options,
        bool allowMultiple, bool anonymous)
    {
        var poll = new Poll
        {
            Question = question,
            CreatedByName = turnContext.Activity.From?.Name ?? "Unbekannt",
            CreatedById = turnContext.Activity.From?.Id ?? "unknown",
            ConversationId = turnContext.Activity.Conversation?.Id ?? string.Empty,
            AllowMultiple = allowMultiple,
            Anonymous = anonymous,
        };

        for (var i = 0; i < options.Count; i++)
        {
            poll.Options.Add(new PollOption
            {
                PollId = poll.Id,
                Text = options[i],
                OrderIndex = i,
            });
        }

        return poll;
    }

    private static async Task<ResourceResponse?> SendCardAsync(
        ITurnContext turnContext, JObject card, CancellationToken cancellationToken)
    {
        var attachment = new Attachment { ContentType = PollCardFactory.ContentType, Content = card };
        return await turnContext.SendActivityAsync(MessageFactory.Attachment(attachment), cancellationToken);
    }

    private static AdaptiveCardInvokeResponse CardResponse(JObject card) => new()
    {
        StatusCode = 200,
        Type = PollCardFactory.ContentType,
        Value = card,
    };

    private static JObject ClosedOrMissingCard()
    {
        return new JObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = new JArray
            {
                new JObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = "Diese Umfrage ist nicht mehr verfügbar.",
                    ["wrap"] = true,
                },
            },
        };
    }

    /// <summary>
    /// Parses the quick-create syntax: "Frage? | Option 1 | Option 2 [--multi] [--anon]".
    /// Returns null when the text is not a valid poll command.
    /// </summary>
    internal static (string Question, List<string> Options, bool AllowMultiple, bool Anonymous)?
        TryParsePollCommand(string text)
    {
        if (!text.Contains('|'))
            return null;

        var allowMultiple = Regex.IsMatch(text, @"(^|\s)--multi(\s|$)", RegexOptions.IgnoreCase);
        var anonymous = Regex.IsMatch(text, @"(^|\s)--anon(\s|$)", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(^|\s)--(multi|anon)(?=\s|$)", " ", RegexOptions.IgnoreCase).Trim();

        // Drop an optional leading "poll" / "umfrage" keyword.
        text = Regex.Replace(text, @"^(poll|umfrage)\s*:?\s*", "", RegexOptions.IgnoreCase).Trim();

        var parts = text.Split('|').Select(p => p.Trim()).ToList();
        var question = parts.FirstOrDefault() ?? string.Empty;
        var options = parts.Skip(1)
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList();

        if (string.IsNullOrWhiteSpace(question) || options.Count < 2)
            return null;

        return (question, options, allowMultiple, anonymous);
    }
}
