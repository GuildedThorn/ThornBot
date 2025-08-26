using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ThornBot.Handlers;

public class CommandHandler(IServiceProvider services)
{
    private readonly DiscordSocketClient _client = services.GetRequiredService<DiscordSocketClient>();
    private readonly InteractionService _interactions = services.GetRequiredService<InteractionService>();
    private readonly IConfiguration config = services.GetRequiredService<IConfiguration>();
    private readonly IServiceProvider _services = services;

    private bool _commandsRegistered = false;

    public async Task InitializeAsync()
    {
        // Load all modules in the assembly
        var interactionService = _services.GetRequiredService<InteractionService>();
        await interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);

        _client.InteractionCreated += HandleInteraction;
        _client.Ready += RegisterCommandsAsync;

        // Optional: Hook into InteractionService for global error logging
        _interactions.SlashCommandExecuted += SlashCommandExecutedAsync;
    }

    private async Task RegisterCommandsAsync()
    {
        if (_commandsRegistered) return;

        // Register to a specific guild (faster updates, good for dev/testing)
        var guild = ulong.Parse(config["discord:developmentGuildId"] ?? 
                                throw new InvalidOperationException("discord developmentGuildId not configured."));
        await _interactions.RegisterCommandsToGuildAsync(guild, true);

        // If you want global commands instead, use:
        // await _interactions.RegisterCommandsGloballyAsync(true);

        _commandsRegistered = true;
        Console.WriteLine("✅ Commands registered.");
    }

    private async Task HandleInteraction(SocketInteraction arg)
    {
        try
        {
            var ctx = new SocketInteractionContext(_client, arg);
            var result = await _interactions.ExecuteCommandAsync(ctx, _services);

            if (!result.IsSuccess)
            {
                Console.WriteLine($"⚠️ Command error: {result.ErrorReason}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex}");

            if (arg.Type == InteractionType.ApplicationCommand)
            {
                try
                {
                    var response = await arg.GetOriginalResponseAsync();
                    await response.DeleteAsync();
                }
                catch
                {
                    // Ignore if the response was never sent
                }
            }
        }
    }

    private Task SlashCommandExecutedAsync(SlashCommandInfo cmd, IInteractionContext ctx, IResult result)
    {
        if (!result.IsSuccess)
        {
            Console.WriteLine($"⚠️ Slash command `{cmd.Name}` failed for {ctx.User}: {result.ErrorReason}");
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Unsubscribe from events if you ever stop/reload the handler
        _client.InteractionCreated -= HandleInteraction;
        _client.Ready -= RegisterCommandsAsync;
        _interactions.SlashCommandExecuted -= SlashCommandExecutedAsync;
    }
}
