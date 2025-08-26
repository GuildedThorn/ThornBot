using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using ThornBot.Handlers;

namespace ThornBot.Modules;

public class UserModule(IConfiguration config) : InteractionModuleBase<SocketInteractionContext> {
    
    [SlashCommand("info", "Get statistics about the bot")]
    public async Task InfoAsync() {
        var platform = System.Environment.OSVersion.Platform;
        var version = System.Environment.OSVersion.Version;
        var uptime = DateTime.Now - ThornBot.StartTime;

        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed("ThornBot",
            "A personal utility bot made by https://guildedthorn.com\n\n" +
            "**Bot Version:** " + config["Version"] + "\n" +
            "**Bot Platform:** " + platform + "\n" +
            "**Bot OS Version:** " + version + "\n" +
            "**Bot Uptime:** " + uptime.ToString(@"dd\.hh\:mm\:ss")));
    }
}