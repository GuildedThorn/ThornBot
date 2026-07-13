using Discord;
using Victoria;

namespace ThornBot.Handlers;

public static class EmbedColors
{
    public static readonly Color Brand = new(88, 101, 242);
    public static readonly Color Success = new(87, 242, 135);
    public static readonly Color Error = Color.Red;
}

public class EmbedHandler {

    public static Task<Embed> CreateBasicEmbed(string title, string description, Color? color = null) {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color ?? EmbedColors.Brand)
            .WithCurrentTimestamp()
            .Build();
        return Task.FromResult(embed);
    }

    public static Task<Embed> CreateBasicEmbedWithFields(string title, string description, EmbedFieldBuilder[] fields) {
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithFields(fields)
            .WithDescription(description)
            .WithColor(EmbedColors.Brand)
            .WithCurrentTimestamp()
            .Build();
        return Task.FromResult(embed);
    }

    public static Task<Embed> CreateErrorEmbed(string error) {
        var embed = new EmbedBuilder()
            .WithTitle("ThornBot | error")
            .WithDescription($"Error: {error}")
            .WithColor(EmbedColors.Error)
            .WithCurrentTimestamp()
            .Build();
        return Task.FromResult(embed);
    }

    // Shared by "now playing", "added to queue", "skipped", etc. — anywhere a
    // LavaTrack needs to be shown.
    public static Task<Embed> CreateTrackEmbed(string headline, string emoji, LavaTrack track,
        IUser? requestedBy = null, Color? color = null) {
        var builder = new EmbedBuilder()
            .WithTitle($"{emoji} {headline}")
            .WithDescription($"[{track.Title}]({track.Url})\n{track.Author}")
            .WithColor(color ?? EmbedColors.Brand)
            .WithCurrentTimestamp()
            .AddField("Duration", track.IsLiveStream ? "🔴 LIVE" : FormatDuration(track.Duration), inline: true);

        if (!string.IsNullOrWhiteSpace(track.Artwork))
            builder.WithThumbnailUrl(track.Artwork);

        if (requestedBy is not null)
            builder.WithFooter($"Requested by {requestedBy.Username}", requestedBy.GetAvatarUrl() ?? requestedBy.GetDefaultAvatarUrl());

        return Task.FromResult(builder.Build());
    }

    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
}
