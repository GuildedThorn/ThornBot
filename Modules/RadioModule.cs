using Discord;
using Discord.Interactions;
using ThornBot.Attributes;
using ThornBot.Handlers;
using ThornBot.Services;
using Victoria;
using Victoria.Rest.Search;

namespace ThornBot.Modules;

[Group("radio", "Radio commands")]
public class RadioModule(LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, AudioService audioService, RadioService radioService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("status", "Shows whether the radio is live and what's playing.")]
    public async Task StatusAsync()
    {
        var name = string.IsNullOrWhiteSpace(radioService.Name) ? "Radio" : radioService.Name;

        if (!radioService.IsLive)
        {
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                $"📻 {name}", "Offline right now."), ephemeral: true);
            return;
        }

        var nowPlaying = string.IsNullOrWhiteSpace(radioService.Artist)
            ? radioService.Title
            : $"{radioService.Artist} - {radioService.Title}";

        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
            $"📻 {name}",
            string.IsNullOrWhiteSpace(nowPlaying)
                ? "Live now!"
                : $"Live now — **{nowPlaying}**"), ephemeral: true);
    }

    [SlashCommand("archive", "Shows past radio broadcasts you can replay.")]
    public async Task ArchiveAsync([Summary("page", "Page number"), MinValue(1)] int page = 1)
    {
        (IReadOnlyList<ArchiveEntry> Items, int TotalPages) archive;
        try
        {
            archive = await radioService.GetArchiveAsync(page);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed($"Couldn't reach the archive: {ex.Message}"), ephemeral: true);
            return;
        }

        if (archive.Items.Count == 0)
        {
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "📼 Radio Archive", "No past broadcasts recorded yet."), ephemeral: true);
            return;
        }

        var lines = archive.Items.Select(item =>
            $"**{item.StartedAt:yyyy-MM-dd HH:mm} UTC** — {EmbedHandler.FormatDuration(TimeSpan.FromSeconds(item.DurationSeconds))} — `{item.Id}`");
        var description = string.Join('\n', lines) +
            $"\n\nPage {page}/{archive.TotalPages} — use `/radio play <id>` to replay one.";

        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed("📼 Radio Archive", description), ephemeral: true);
    }

    [SlashCommand("play", "Plays a past radio broadcast."), RequireRadioNotLive]
    public async Task PlayAsync([Summary("id", "Recording ID from /radio archive")] string id)
    {
        await DeferAsync(ephemeral: true);

        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player == null)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState?.VoiceChannel == null)
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed("You must be connected to a voice channel!"), ephemeral: true);
                return;
            }

            try
            {
                player = await lavaNode.JoinAsync(voiceState.VoiceChannel);
                audioService.TextChannels[Context.Guild.Id] = Context.Channel.Id;
            }
            catch (Exception exception)
            {
                await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed($"Failed to join: {exception.Message}"), ephemeral: true);
                return;
            }
        }

        var searchResponse = await lavaNode.LoadTrackAsync($"{radioService.BaseUrl}/api/radio/recordings/{id}/stream");
        if (searchResponse.Type is SearchType.Empty or SearchType.Error)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed($"Couldn't find a recording with id `{id}`."), ephemeral: true);
            return;
        }

        var track = searchResponse.Tracks.First();
        var startedNow = await audioService.PlayOrEnqueueAsync(player, lavaNode, track, Context.User);

        await FollowupAsync(embed: startedNow
            ? await EmbedHandler.CreateTrackEmbed("Starting Playback", "📼", track, Context.User)
            : await EmbedHandler.CreateTrackEmbed("Added to Queue", "📼", track, Context.User), ephemeral: true);
    }
}
