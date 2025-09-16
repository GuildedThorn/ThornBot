using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ThornBot.Services;
using Victoria;

namespace ThornBot.Handlers;

public class EventsHandler(IServiceProvider serviceProvider) {

    public async Task OnReadyAsync() {
        await serviceProvider.UseLavaNodeAsync();
        Log.Information("✅ Lava Link Connected!");
        
        var icecast = serviceProvider.GetRequiredService<IcecastService>();
        _ = icecast.StartMonitoringAsync();
        Log.Information("✅ Icecast 2 service started successfully!");
        
        var guestBookService = serviceProvider.GetRequiredService<GuestBookService>();
        _ = guestBookService.StartAsync();
        Log.Information("✅ GuestBookService started successfully!");
        
        var trueNasService = serviceProvider.GetRequiredService<TrueNasService>();
        trueNasService.StartMonitoring();
        Log.Information("✅ TrueNAS monitoring service started successfully!");
        
        var uptimeService = serviceProvider.GetRequiredService<UptimeService>();
        _ = uptimeService.StartMonitoringAsync();
        Log.Information("✅ Uptime monitoring service started successfully!");
    }
}