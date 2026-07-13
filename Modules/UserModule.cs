using System.Diagnostics;
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
        var uptime = DateTime.Now - ThornBot.StartTime;
        var memoryMb = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;

        var fields = new[]
        {
            new EmbedFieldBuilder().WithName("Version").WithValue(config["Version"] ?? "unknown").WithIsInline(true),
            new EmbedFieldBuilder().WithName(".NET").WithValue(Environment.Version.ToString()).WithIsInline(true),
            new EmbedFieldBuilder().WithName("OS").WithValue($"{Environment.OSVersion.Platform} {Environment.OSVersion.Version}").WithIsInline(true),
            new EmbedFieldBuilder().WithName("Uptime").WithValue(uptime.ToString(@"dd\.hh\:mm\:ss")).WithIsInline(true),
            new EmbedFieldBuilder().WithName("Ping").WithValue($"{Context.Client.Latency}ms").WithIsInline(true),
            new EmbedFieldBuilder().WithName("Memory").WithValue($"{memoryMb:F0} MB").WithIsInline(true),
            new EmbedFieldBuilder().WithName("Guilds").WithValue(Context.Client.Guilds.Count.ToString()).WithIsInline(true),
            new EmbedFieldBuilder().WithName("Users").WithValue(Context.Client.Guilds.Sum(g => g.MemberCount).ToString()).WithIsInline(true),
            new EmbedFieldBuilder().WithName("Channels").WithValue(Context.Client.Guilds.Sum(g => g.Channels.Count).ToString()).WithIsInline(true),
        };

        var embed = await EmbedHandler.CreateBasicEmbedWithFields(
            "🤖 ThornBot",
            "A personal utility bot by [GuildedThorn](https://guildedthorn.com).",
            fields,
            Context.Client.CurrentUser.GetAvatarUrl() ?? Context.Client.CurrentUser.GetDefaultAvatarUrl());

        var components = new ComponentBuilder()
            .WithButton("View Source", style: ButtonStyle.Link, url: "https://github.com/GuildedThorn/ThornBot")
            .Build();

        await RespondAsync(embed: embed, components: components, ephemeral: true);
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
        if (!ulong.TryParse(config["radio:songRequestGuildId"], out var logGuildId) ||
            !ulong.TryParse(config["radio:songRequestChannelId"], out var logChannelId))
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