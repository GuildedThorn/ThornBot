using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using ThornBot.Attributes;
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

    [SlashCommand("join", "Makes the bot join your voice channel.")]
    public async Task JoinAsync()
    {
        if (Context.Guild == null)
        {
            await RespondAsync("This command can only be used in a server!");
            return;
        }

        var guildUser = Context.User as SocketGuildUser;
        if (guildUser?.VoiceChannel == null)
        {
            await RespondAsync("You must be connected to a voice channel!");
            return;
        }

        try
        {
            await _lavaNode.JoinAsync(guildUser.VoiceChannel);
            await RespondAsync($"Joined {guildUser.VoiceChannel.Name}!");
            _audioService.TextChannels.TryAdd(Context.Guild.Id, Context.Channel.Id);
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ {ex.Message}");
        }
    }

    [SlashCommand("resume", "Resume the current song in the queue"), RequirePlayer]
    public async Task ResumeAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);


        try {
            if (player is { IsPaused: false, Track: not null }) {
                await player.ResumeAsync(lavaNode, player.Track);

                await RespondAsync($"Resumed: {player.Track.Title}", ephemeral: true);
            }
            else {
                await RespondAsync("I cannot resume when I'm not playing anything!", ephemeral: true);
            }
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }

    [SlashCommand("leave", "Makes the bot leave the voice channel.")]
    public async Task LeaveAsync()
    {
        var guildUser = Context.User as SocketGuildUser;
        var voiceChannel = guildUser?.VoiceChannel;

        if (voiceChannel == null)
        {
            await RespondAsync("Not sure which voice channel to disconnect from.", ephemeral: true);
            return;
        }

        try
        {
            await _lavaNode.LeaveAsync(voiceChannel);
            await RespondAsync($"I've left {voiceChannel.Name}!", ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ {ex.Message}");
        }
    }

    [SlashCommand("play", "Plays a song.")]
    public async Task PlayAsync([Remainder] string searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            await RespondAsync("Please provide search terms.", ephemeral: true);
            return;
        }

        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player == null)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState?.VoiceChannel == null)
            {
                await RespondAsync("You must be connected to a voice channel!", ephemeral: true);
                return;
            }

            try
            {
                player = await lavaNode.JoinAsync(voiceState.VoiceChannel);
                await RespondAsync($"Joined {voiceState.VoiceChannel.Name}!", ephemeral: true);
                audioService.TextChannels.TryAdd(Context.Guild.Id, Context.Channel.Id);
            }
            catch (Exception exception)
            {
                await ReplyAsync($"Failed to join: {exception.Message}");
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
            await RespondAsync($"I wasn't able to find anything for `{searchQuery}`.", ephemeral: true);
            return;
        }

        var track = searchResponse.Tracks.First();

        if (player.GetQueue().Count == 0)
        {
            await player.PlayAsync(lavaNode, track);
            await RespondAsync($"Now playing: {track.Title}", ephemeral: true);
        }
        else
        {
            player.GetQueue().Enqueue(track);
            await RespondAsync($"Added {track.Title} to queue.", ephemeral: true);
        }
    }


    [SlashCommand("stop", "Stops the current song and clears the queue."), RequirePlayer]
    public async Task StopAsync()
    {
        var player = await _lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected || player.Track == null)
        {
            await RespondAsync("Woah, can't stop won't stop.", ephemeral: true);
            return;
        }

        try
        {
            await player.StopAsync(_lavaNode, player.Track);
            await RespondAsync("No longer playing anything.", ephemeral: true);
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }

    [SlashCommand("skip", "Skip the current playing song in the queue"), RequirePlayer]
    public async Task SkipAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected)
        {
            await RespondAsync(" Woaaah there, I can't skip when nothing is playing.", ephemeral: true);
            return;
        }

        var voiceChannelUsers = Context.Guild.CurrentUser.VoiceChannel
            .Users
            .Where(x => !x.IsBot)
            .ToArray();

        if (!audioService.VoteQueue.Add(Context.User.Id))
        {
            await RespondAsync("You can't vote again.", ephemeral: true);
            return;
        }

        var percentage = audioService.VoteQueue.Count / voiceChannelUsers.Length * 100;
        if (percentage < 85)
        {
            await RespondAsync("You need more than 85% votes to skip this song.", ephemeral: true);
            return;
        }

        try
        {
            var (skipped, currenTrack) = await player.SkipAsync(lavaNode);
            await RespondAsync($"Skipped: {skipped.Title}\nNow Playing: {currenTrack.Title}", ephemeral: true);
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }
}