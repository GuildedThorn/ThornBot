using Victoria;

namespace ThornBot.Handlers;

public class EventsHandler(IServiceProvider serviceProvider) {

    public async Task OnReadyAsync() {
        await serviceProvider.UseLavaNodeAsync();
        Console.WriteLine("✅ Lava Link Connected!");

    }
}