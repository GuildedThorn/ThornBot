using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ThornBot.Handlers;

public class CommandHandler(IServiceProvider services) {
    
    private readonly DiscordSocketClient _client = services.GetRequiredService<DiscordSocketClient>();
    private readonly InteractionService _interactions = services.GetRequiredService<InteractionService>();
    private readonly IConfiguration _config = services.GetRequiredService<IConfiguration>();

    private bool _commandsRegistered;

    public async Task InitializeAsync() {
        // Load all modules in the assembly
        var interactionService = services.GetRequiredService<InteractionService>();
        await interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), services);

        _client.InteractionCreated += HandleInteraction;
        _client.Ready += RegisterCommandsAsync;

        // Optional: Hook into InteractionService for global error logging
        _interactions.SlashCommandExecuted += SlashCommandExecutedAsync;
    }

    private async Task RegisterCommandsAsync() {
        if (_commandsRegistered) return;

        // Register to a specific guild (faster updates, good for dev/testing)
        var guild = ulong.Parse(_config["discord:developmentGuildId"] ?? 
                                throw new InvalidOperationException("discord developmentGuildId not configured."));
        await _interactions.RegisterCommandsToGuildAsync(guild);

        // If you want global commands instead, use:
        // await _interactions.RegisterCommandsGloballyAsync(true);

        _commandsRegistered = true;
        Log.Information("✅ Commands registered.");
    }

    private async Task HandleInteraction(SocketInteraction arg) {
        try {
            var ctx = new SocketInteractionContext(_client, arg);
            var result = await _interactions.ExecuteCommandAsync(ctx, services);

            if (!result.IsSuccess) {
                Log.Error("⚠️ Command error: {ResultErrorReason}", result.ErrorReason);
            }
        }
        catch (Exception ex) {
            Log.Error("❌ Exception: {Exception}", ex);

            if (arg.Type == InteractionType.ApplicationCommand) {
                try {
                    var response = await arg.GetOriginalResponseAsync();
                    await response.DeleteAsync();
                }
                catch {
                    // ignored
                }
            }
        }
    }
    
    private static Task SlashCommandExecutedAsync(SlashCommandInfo cmd, IInteractionContext ctx, IResult result) {
        if (!result.IsSuccess) {
            Log.Error("⚠️ Slash command `{CmdName}` failed for '{CtxUser}': '{ResultErrorReason}'", 
                cmd.Name, ctx.User, result.ErrorReason);
        }
        return Task.CompletedTask;
    }

    public void Dispose() {
        // Unsubscribe from events if you ever stop/reload the handler
        _client.InteractionCreated -= HandleInteraction;
        _client.Ready -= RegisterCommandsAsync;
        _interactions.SlashCommandExecuted -= SlashCommandExecutedAsync;
    }
}
