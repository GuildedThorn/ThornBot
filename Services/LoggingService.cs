using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace ThornBot.Services;

public class LoggingService {
    
    public LoggingService(IServiceProvider services) {
        var client = services.GetRequiredService<DiscordSocketClient>();
        var interactionService = services.GetRequiredService<InteractionService>();
        
        client.Log += OnLogAsync;
        interactionService.Log += OnLogAsync;
    }
    
    private static Task OnLogAsync(LogMessage message) {
        var msg = $"{DateTime.Now,-19} {message.Source}: {message.Message} {message.Exception}";

        Console.ForegroundColor = message.Severity switch
        {
            LogSeverity.Critical or LogSeverity.Error => ConsoleColor.Red,
            LogSeverity.Warning => ConsoleColor.Yellow,
            LogSeverity.Debug or LogSeverity.Verbose => ConsoleColor.DarkGray,
            _ => ConsoleColor.Green
        };
        Console.WriteLine(msg);
        Console.ResetColor();
        return Task.CompletedTask;
    }
}