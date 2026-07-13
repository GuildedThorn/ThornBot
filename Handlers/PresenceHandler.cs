using Discord;
using Discord.WebSocket;

namespace ThornBot.Handlers;

public static class PresenceHandler
{
    private const string DefaultActivity = "/play";

    public static Task SetDefaultAsync(DiscordSocketClient client) =>
        client.SetGameAsync(DefaultActivity, type: ActivityType.Listening);

    public static Task SetListeningAsync(DiscordSocketClient client, string activity) =>
        client.SetGameAsync(activity, type: ActivityType.Listening);
}
