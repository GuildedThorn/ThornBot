using Microsoft.Extensions.DependencyInjection;
using ThornBot.Services;
using Victoria;

namespace ThornBot.Handlers;

public class EventsHandler(IServiceProvider serviceProvider) {

    public async Task OnReadyAsync() {
        await serviceProvider.UseLavaNodeAsync();
        Console.WriteLine("✅ Lava Link Connected!");
        
        var radio = serviceProvider.GetRequiredService<RadioService>();
        _ = radio.StartMonitoringAsync();
        Console.WriteLine("✅ Radio service started successfully!");
        
        var guestBookService = serviceProvider.GetRequiredService<GuestBookService>();
        _ = guestBookService.StartAsync();
        Console.WriteLine("✅ GuestBookService started successfully!");
        
        var uptimeService = serviceProvider.GetRequiredService<UptimeService>();
        _ = uptimeService.StartMonitoringAsync();
        Console.WriteLine("✅ Uptime monitoring service started successfully!");
        
    }
}