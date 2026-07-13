using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using ThornBot.Attributes;
using ThornBot.Handlers;
using ThornBot.Services;
using Victoria;
using Victoria.Rest.Search;

namespace ThornBot.Modules;

public class AudioModule(LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, AudioService audioService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode =
        lavaNode ?? throw new ArgumentNullException(nameof(lavaNode));

    private readonly AudioService _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

    [SlashCommand("join", "Makes the bot join your voice channel."), RequireRadioNotLive]
    public async Task JoinAsync()
    {
        if (Context.Guild == null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("This command can only be used in a server!"), ephemeral: true);
            return;
        }

        var guildUser = Context.User as SocketGuildUser;
        if (guildUser?.VoiceChannel == null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("You must be connected to a voice channel!"), ephemeral: true);
            return;
        }

        try
        {
            await _lavaNode.JoinAsync(guildUser.VoiceChannel);
            _audioService.TextChannels[Context.Guild.Id] = Context.Channel.Id;
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "✅ Joined", $"Connected to **{guildUser.VoiceChannel.Name}**.", EmbedColors.Success));
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("resume", "Resume the current song in the queue"), RequirePlayer, RequireRadioNotLive]
    public async Task ResumeAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);

        try {
            if (player is { IsPaused: true, Track: not null }) {
                await player.ResumeAsync(lavaNode, player.Track);
                await RespondAsync(embed: await EmbedHandler.CreateTrackEmbed(
                    "Now Playing", "🎶", player.Track, audioService.GetRequester(player.Track)), ephemeral: true);
            }
            else {
                await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(
                    "I'm not paused right now."), ephemeral: true);
            }
        }
        catch (Exception exception)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(exception.Message), ephemeral: true);
        }
    }

    [SlashCommand("leave", "Makes the bot leave the voice channel."), RequireRadioNotLive]
    public async Task LeaveAsync()
    {
        var guildUser = Context.User as SocketGuildUser;
        var voiceChannel = guildUser?.VoiceChannel;

        if (voiceChannel == null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(
                "Not sure which voice channel to disconnect from."), ephemeral: true);
            return;
        }

        try
        {
            await _lavaNode.LeaveAsync(voiceChannel);
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "👋 Left", $"Disconnected from **{voiceChannel.Name}**.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("play", "Plays a song."), RequireRadioNotLive]
    public async Task PlayAsync([Remainder] string searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("Please provide search terms."), ephemeral: true);
            return;
        }

        // Searching can take a moment — avoid the 3s interaction ack timeout.
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

        SearchResponse searchResponse;

        // Detect URL vs. search query
        if (Uri.IsWellFormedUriString(searchQuery, UriKind.Absolute))
        {
            searchResponse = await lavaNode.LoadTrackAsync(searchQuery); // Direct URL
        }
        else if (searchQuery.StartsWith("sc:")) // Optional prefix for explicit SoundCloud search
        {
            var scQuery = searchQuery[3..].Trim();
            searchResponse = await lavaNode.LoadTrackAsync($"scsearch:{scQuery}");
        }
        else
        {
            searchResponse = await lavaNode.LoadTrackAsync($"ytsearch:{searchQuery}"); // Default YouTube search
        }

        if (searchResponse.Type is SearchType.Empty or SearchType.Error)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed($"I wasn't able to find anything for `{searchQuery}`."), ephemeral: true);
            return;
        }

        var track = searchResponse.Tracks.First();
        audioService.SetRequester(track, Context.User);

        // player.GetQueue() only reflects tracks waiting behind the current one —
        // checking it alone would replace whatever's actively playing right now.
        if (player.Track is null)
        {
            await player.PlayAsync(lavaNode, track);
            await FollowupAsync(embed: await EmbedHandler.CreateTrackEmbed("Starting Playback", "▶️", track, Context.User), ephemeral: true);
        }
        else
        {
            player.GetQueue().Enqueue(track);
            await FollowupAsync(embed: await EmbedHandler.CreateTrackEmbed("Added to Queue", "➕", track, Context.User), ephemeral: true);
        }
    }


    [SlashCommand("stop", "Stops the current song and clears the queue."), RequirePlayer, RequireRadioNotLive]
    public async Task StopAsync()
    {
        var player = await _lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected || player.Track == null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("I'm not playing anything."), ephemeral: true);
            return;
        }

        try
        {
            audioService.ClearRequester(player.Track);
            await player.StopAsync(_lavaNode, player.Track);
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "⏹️ Stopped", "Playback stopped and the queue was cleared.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception exception)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(exception.Message), ephemeral: true);
        }
    }

    [SlashCommand("skip", "Skip the current playing song in the queue"), RequirePlayer, RequireRadioNotLive]
    public async Task SkipAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("I can't skip when nothing is playing."), ephemeral: true);
            return;
        }

        var voiceChannelUsers = Context.Guild.CurrentUser.VoiceChannel
            .Users
            .Where(x => !x.IsBot)
            .ToArray();

        if (!audioService.VoteQueue.Add(Context.User.Id))
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("You can't vote again."), ephemeral: true);
            return;
        }

        // Nobody left to vote (bot's alone in the channel) — just let it through.
        var percentage = voiceChannelUsers.Length == 0
            ? 100
            : (double)audioService.VoteQueue.Count / voiceChannelUsers.Length * 100;

        if (percentage < 85)
        {
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🗳️ Vote to Skip",
                $"{audioService.VoteQueue.Count}/{voiceChannelUsers.Length} votes — need 85% to skip."), ephemeral: true);
            return;
        }

        audioService.VoteQueue.Clear();

        try
        {
            var (skipped, current) = await player.SkipAsync(lavaNode);
            audioService.ClearRequester(skipped);
            await RespondAsync(embed: await EmbedHandler.CreateTrackEmbed(
                "Now Playing", "⏭️", current, audioService.GetRequester(current)), ephemeral: true);
        }
        catch (InvalidOperationException)
        {
            // Nothing queued behind it — skipping the last track just stops playback.
            if (player.Track is not null)
            {
                audioService.ClearRequester(player.Track);
                await player.StopAsync(lavaNode, player.Track);
            }
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "⏹️ Queue Finished", "That was the last track — nothing left to play.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception exception)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(exception.Message), ephemeral: true);
        }
    }
}
