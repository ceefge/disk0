using Newtonsoft.Json.Linq;
using TeamsPoll.Server.Models;

namespace TeamsPoll.Server.Cards;

/// <summary>
/// Builds the Adaptive Cards used by the bot. Cards are constructed as JObject
/// so we can use Teams-only features (Action.Execute universal actions, the
/// <c>refresh</c> block for automatic live updates and the <c>msteams</c>
/// full-width hint) that the strongly-typed AdaptiveCards model does not expose.
/// </summary>
public static class PollCardFactory
{
    public const string ContentType = "application/vnd.microsoft.card.adaptive";

    // Verbs carried by Action.Execute so the bot knows what a tap means.
    public const string VerbVote = "vote";
    public const string VerbRefresh = "refresh";
    public const string VerbClose = "close";
    public const string VerbCreate = "create";
    public const string VerbCreateForm = "createform";

    private const int BarSegments = 20;

    /// <summary>The live voting card: question, per-option bars, footer actions.</summary>
    public static JObject BuildPollCard(Poll poll, PollResults results)
    {
        var body = new JArray
        {
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "📊 " + poll.Question,
                ["weight"] = "Bolder",
                ["size"] = "Large",
                ["wrap"] = true,
            },
            BuildSubtitle(poll, results),
        };

        foreach (var option in results.Options.OrderBy(o => o.OrderIndex))
            body.Add(BuildOptionRow(poll, option, results.TotalVotes));

        // Footer / actions.
        body.Add(new JObject
        {
            ["type"] = "TextBlock",
            ["text"] = poll.Closed
                ? "🔒 Diese Umfrage ist geschlossen."
                : "Tippe auf eine Option, um abzustimmen. Erneutes Tippen nimmt die Stimme zurück.",
            ["wrap"] = true,
            ["isSubtle"] = true,
            ["size"] = "Small",
            ["spacing"] = "Medium",
        });

        if (!poll.Closed)
            body.Add(BuildActionSet(poll));

        var card = new JObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
            ["msteams"] = new JObject { ["width"] = "Full" },
        };

        // Auto-refresh: Teams re-runs the refresh action for listed users when they
        // view the card, so results stay live without anyone tapping "Aktualisieren".
        if (!poll.Closed)
            card["refresh"] = BuildRefresh(poll, results);

        return card;
    }

    private static JObject BuildSubtitle(Poll poll, PollResults results)
    {
        var parts = new List<string> { $"von {poll.CreatedByName}" };
        parts.Add(results.TotalVoters == 1 ? "1 Teilnehmer" : $"{results.TotalVoters} Teilnehmer");
        if (poll.AllowMultiple) parts.Add("Mehrfachauswahl");
        if (poll.Anonymous) parts.Add("anonym");

        return new JObject
        {
            ["type"] = "TextBlock",
            ["text"] = string.Join("  ·  ", parts),
            ["isSubtle"] = true,
            ["size"] = "Small",
            ["spacing"] = "None",
            ["wrap"] = true,
        };
    }

    private static JObject BuildOptionRow(Poll poll, OptionResult option, int totalVotes)
    {
        var pct = totalVotes > 0 ? (int)Math.Round(option.Count * 100.0 / totalVotes) : 0;

        var items = new JArray
        {
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = option.Text,
                ["weight"] = "Bolder",
                ["wrap"] = true,
                ["spacing"] = "None",
            },
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = $"{ProgressBar(pct)}  {option.Count} · {pct}%",
                ["fontType"] = "Monospace",
                ["wrap"] = false,
                ["spacing"] = "None",
                ["size"] = "Small",
            },
        };

        // Show who voted (WhatsApp-style) unless the poll is anonymous.
        if (!poll.Anonymous && option.VoterNames.Count > 0)
        {
            items.Add(new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = string.Join(", ", option.VoterNames),
                ["isSubtle"] = true,
                ["size"] = "Small",
                ["wrap"] = true,
                ["spacing"] = "None",
            });
        }

        var container = new JObject
        {
            ["type"] = "Container",
            ["spacing"] = "Medium",
            ["style"] = "emphasis",
            ["items"] = items,
        };

        // The whole row is tappable while the poll is open.
        if (!poll.Closed)
        {
            container["selectAction"] = new JObject
            {
                ["type"] = "Action.Execute",
                ["verb"] = VerbVote,
                ["title"] = option.Text,
                ["data"] = new JObject
                {
                    ["pollId"] = poll.Id,
                    ["optionId"] = option.OptionId,
                },
            };
        }

        return container;
    }

    private static JObject BuildActionSet(Poll poll)
    {
        var actions = new JArray
        {
            new JObject
            {
                ["type"] = "Action.Execute",
                ["verb"] = VerbRefresh,
                ["title"] = "🔄 Aktualisieren",
                ["data"] = new JObject { ["pollId"] = poll.Id },
            },
            new JObject
            {
                ["type"] = "Action.Execute",
                ["verb"] = VerbClose,
                ["title"] = "🔒 Schließen",
                ["data"] = new JObject { ["pollId"] = poll.Id },
            },
        };

        return new JObject
        {
            ["type"] = "ActionSet",
            ["spacing"] = "Small",
            ["actions"] = actions,
        };
    }

    private static JObject BuildRefresh(Poll poll, PollResults results)
    {
        // Teams allows up to 60 user ids for automatic refresh.
        var userIds = new JArray();
        foreach (var id in results.ParticipantIds.Take(60))
            userIds.Add(id);

        return new JObject
        {
            ["action"] = new JObject
            {
                ["type"] = "Action.Execute",
                ["verb"] = VerbRefresh,
                ["title"] = "Refresh",
                ["data"] = new JObject { ["pollId"] = poll.Id },
            },
            ["userIds"] = userIds,
        };
    }

    /// <summary>Form for creating a poll interactively (opened from the help card).</summary>
    public static JObject BuildCreateForm()
    {
        var body = new JArray
        {
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "➕ Neue Umfrage",
                ["weight"] = "Bolder",
                ["size"] = "Large",
            },
            new JObject { ["type"] = "TextBlock", ["text"] = "Frage", ["weight"] = "Bolder", ["spacing"] = "Medium" },
            new JObject
            {
                ["type"] = "Input.Text",
                ["id"] = "question",
                ["placeholder"] = "Worüber soll abgestimmt werden?",
                ["maxLength"] = 280,
            },
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "Antwortoptionen (eine pro Zeile)",
                ["weight"] = "Bolder",
                ["spacing"] = "Medium",
            },
            new JObject
            {
                ["type"] = "Input.Text",
                ["id"] = "options",
                ["isMultiline"] = true,
                ["placeholder"] = "Option 1\nOption 2\nOption 3",
            },
            new JObject
            {
                ["type"] = "Input.Toggle",
                ["id"] = "multi",
                ["title"] = "Mehrfachauswahl erlauben",
                ["value"] = "false",
                ["valueOn"] = "true",
                ["valueOff"] = "false",
                ["spacing"] = "Medium",
            },
            new JObject
            {
                ["type"] = "Input.Toggle",
                ["id"] = "anon",
                ["title"] = "Anonyme Abstimmung",
                ["value"] = "false",
                ["valueOn"] = "true",
                ["valueOff"] = "false",
            },
        };

        var actions = new JArray
        {
            new JObject
            {
                ["type"] = "Action.Execute",
                ["verb"] = VerbCreate,
                ["title"] = "Umfrage starten",
                ["style"] = "positive",
            },
        };

        return new JObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
            ["actions"] = actions,
            ["msteams"] = new JObject { ["width"] = "Full" },
        };
    }

    /// <summary>Help / welcome card shown when the bot is added or asked for help.</summary>
    public static JObject BuildHelpCard()
    {
        var body = new JArray
        {
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "📊 Team-Umfragen",
                ["weight"] = "Bolder",
                ["size"] = "Large",
            },
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "So einfach wie bei WhatsApp: Frage stellen, alle tippen auf ihre Antwort, das Ergebnis aktualisiert sich live.",
                ["wrap"] = true,
                ["spacing"] = "Small",
            },
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "**Schnell per Nachricht:**",
                ["wrap"] = true,
                ["spacing"] = "Medium",
            },
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "`Pizza heute Abend? | Ja | Nein | Vielleicht`",
                ["wrap"] = true,
                ["fontType"] = "Monospace",
                ["spacing"] = "None",
            },
            new JObject
            {
                ["type"] = "TextBlock",
                ["text"] = "Optionen mit `|` trennen. Zusätze: `--multi` (Mehrfachauswahl), `--anon` (anonym).",
                ["wrap"] = true,
                ["isSubtle"] = true,
                ["size"] = "Small",
                ["spacing"] = "None",
            },
        };

        var actions = new JArray
        {
            new JObject
            {
                ["type"] = "Action.Execute",
                ["verb"] = VerbCreateForm,
                ["title"] = "➕ Neue Umfrage",
                ["style"] = "positive",
            },
        };

        return new JObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body,
            ["actions"] = actions,
            ["msteams"] = new JObject { ["width"] = "Full" },
        };
    }

    private static string ProgressBar(int pct)
    {
        var filled = (int)Math.Round(pct / 100.0 * BarSegments);
        filled = Math.Clamp(filled, 0, BarSegments);
        return new string('█', filled) + new string('░', BarSegments - filled);
    }
}
