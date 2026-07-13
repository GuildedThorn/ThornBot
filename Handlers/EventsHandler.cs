using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using ThornBot.Services;
using Victoria;

namespace ThornBot.Handlers;

public class EventsHandler(IServiceProvider serviceProvider) {

    public async Task OnReadyAsync() {
        await serviceProvider.UseLavaNodeAsync();
        Console.WriteLine("✅ Lava Link Connected!");

        await PresenceHandler.SetDefaultAsync(serviceProvider.GetRequiredService<DiscordSocketClient>());

        // Isolated so one dependency being unreachable (e.g. RabbitMQ down)
        // can't abort this method and silently skip the services after it.
        StartService("Radio", () => {
            var radio = serviceProvider.GetRequiredService<RadioService>();
            _ = radio.StartMonitoringAsync();
        });

        StartService("GuestBookService", () => {
            var guestBookService = serviceProvider.GetRequiredService<GuestBookService>();
            _ = guestBookService.StartAsync();
        });

        StartService("Uptime monitoring", () => {
            var uptimeService = serviceProvider.GetRequiredService<UptimeService>();
            _ = uptimeService.StartMonitoringAsync();
        });
    }

    private static void StartService(string name, Action start) {
        try {
            start();
            Console.WriteLine($"✅ {name} service started successfully!");
        } catch (Exception ex) {
            Console.WriteLine($"❌ {name} service failed to start: {ex.Message}");
        }
    }
}