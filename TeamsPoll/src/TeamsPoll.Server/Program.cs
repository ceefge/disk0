using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using TeamsPoll.Server.Bot;
using TeamsPoll.Server.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Bot Framework wiring -------------------------------------------------
// Reads MicrosoftAppId / MicrosoftAppPassword / MicrosoftAppType / MicrosoftAppTenantId
// from configuration. With empty AppId it works against the local Bot Framework Emulator.
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

// The adapter is the bridge between HTTP and the bot. Our subclass adds error handling.
builder.Services.AddSingleton<CloudAdapter, AdapterWithErrorHandler>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter>(sp => sp.GetRequiredService<CloudAdapter>());

// Data access: one shared repository over a single SQLite file.
builder.Services.AddSingleton<PollRepository>();

// The bot itself. Transient: a fresh instance handles each turn.
builder.Services.AddTransient<IBot, PollBot>();

var app = builder.Build();

// Ensure the database schema exists before the first request.
await app.Services.GetRequiredService<PollRepository>().InitializeAsync();

// Bot messaging endpoint that Teams / the Emulator posts activities to.
app.MapPost("/api/messages",
    async (HttpRequest request, HttpResponse response, IBotFrameworkHttpAdapter adapter, IBot bot) =>
        await adapter.ProcessAsync(request, response, bot));

// Simple liveness probe for load balancers / uptime checks.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "TeamsPoll" }));

app.Run();
