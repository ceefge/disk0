using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;

namespace TeamsPoll.Server.Bot;

/// <summary>
/// CloudAdapter with a turn-level error handler so a single failing activity
/// never takes down the bot and the user gets a friendly message.
/// </summary>
public class AdapterWithErrorHandler : CloudAdapter
{
    public AdapterWithErrorHandler(
        BotFrameworkAuthentication auth,
        ILogger<CloudAdapter> logger)
        : base(auth, logger)
    {
        OnTurnError = async (turnContext, exception) =>
        {
            logger.LogError(exception, "[OnTurnError] unhandled error: {Message}", exception.Message);

            await turnContext.SendActivityAsync(
                "Ups – da ist etwas schiefgelaufen. Bitte versuche es noch einmal.");
        };
    }
}
