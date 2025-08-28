using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Configuration;
using ThornBot.Handlers;

namespace ThornBot.Modules;

public class UserModule(IConfiguration config) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("info", "Get statistics about the bot")]
    public async Task InfoAsync()
    {
        var platform = Environment.OSVersion.Platform;
        var version = Environment.OSVersion.Version;
        var uptime = DateTime.Now - ThornBot.StartTime;

        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed("ThornBot",
            "A personal utility bot made by https://guildedthorn.com\n\n" +
            "**Bot Version:** " + config["Version"] + "\n" +
            "**Bot Platform:** " + platform + "\n" +
            "**Bot OS Version:** " + version + "\n" +
            "**Total Guilds:** " + Context.Client.Guilds.Count + "\n" +
            "**Total Users:** " + Context.Client.Guilds.Sum(g => g.MemberCount) + "\n" +
            "**Total Channels:** " + Context.Client.Guilds.Sum(g => g.Channels.Count) + "\n" +
            $"**Ping:** {Context.Client.Latency}" + "ms" + "\n" +
            "**Bot Uptime:** " + uptime.ToString(@"dd\.hh\:mm\:ss")), ephemeral: true);
    }

    [SlashCommand("songrequest", "Request a song to be in the ThornRadio mix")]
    public async Task SongRequestAsync([Summary("song", "The song you want to request")] string song)
    {
        // Validate song length
        if (string.IsNullOrWhiteSpace(song) || song.Length > 200)
        {
            await RespondAsync(
                embed: await EmbedHandler.CreateErrorEmbed("Your song request is too long or empty! Please keep it under 200 characters."
                ),
                ephemeral: true
            );
            return;
        }

        // Parse configured log guild and channel IDs
        if (!ulong.TryParse(config["icecast:songRequestGuildId"], out var logGuildId) ||
            !ulong.TryParse(config["icecast:songRequestChannelId"], out var logChannelId))
        {
            await RespondAsync(
                embed: await EmbedHandler.CreateErrorEmbed("The song request log guild or channel is not configured properly."
                ),
                ephemeral: true
            );
            return;
        }

        // Get the log channel from the client
        if (Context.Client.GetChannel(logChannelId) is not IMessageChannel logChannel ||
            (logChannel as ITextChannel)?.Guild.Id != logGuildId)
        {
            await RespondAsync(
                embed: await EmbedHandler.CreateErrorEmbed("Could not find the song request log channel in the configured guild."
                ),
                ephemeral: true
            );
            return;
        }

        // Send the song request to the log channel
        var guildName = Context.Guild?.Name ?? "DM";
        var guildId = Context.Guild?.Id.ToString() ?? "DM";

        var embed = await EmbedHandler.CreateBasicEmbed(
            "New Song Request",
            $"**User:** {Context.User.Username}\n" +
            $"**User ID:** {Context.User.Id}\n" +
            $"**Guild:** {guildName}\n" +
            $"**Guild ID:** {guildId}\n" +
            $"**Song:** {song}"
        );

        await logChannel.SendMessageAsync(embed: embed);

        // Acknowledge the user's request
        await RespondAsync(
            embed: await EmbedHandler.CreateBasicEmbed(
                "Song Request",
                $"Your song request for **{song}** has been received!"
            ),
            ephemeral: true
        );
    }
}