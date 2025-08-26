using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using ThornBot.Attributes;
using ThornBot.Services;
using Victoria;
using Victoria.Rest.Search;

namespace ThornBot.Modules;

public class AudioModule(
    LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
    AudioService audioService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode = lavaNode ?? throw new ArgumentNullException(nameof(lavaNode));
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

        if (_audioService?.TextChannels == null)
        {
            await RespondAsync("AudioService is not properly initialized.");
            return;
        }

        try
        {
            var player = await _lavaNode.JoinAsync(guildUser.VoiceChannel);
            await RespondAsync($"Joined {guildUser.VoiceChannel.Name}!");
            _audioService.TextChannels.TryAdd(Context.Guild.Id, Context.Channel.Id);
        }
        catch (Exception ex)
        {
            await RespondAsync($"❌ {ex.Message}");
        }
    }

    [SlashCommand("leave", "Makes the bot leave the voice channel.")]
    public async Task LeaveAsync()
    {
        var guildUser = Context.User as SocketGuildUser;
        var voiceChannel = guildUser?.VoiceChannel;

        if (voiceChannel == null)
        {
            await RespondAsync("Not sure which voice channel to disconnect from.");
            return;
        }

        try
        {
            await _lavaNode.LeaveAsync(voiceChannel);
            await RespondAsync($"I've left {voiceChannel.Name}!");
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
            await ReplyAsync("Please provide search terms.");
            return;
        }

        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player == null) {
            var voiceState = Context.User as IVoiceState;
            if (voiceState?.VoiceChannel == null) {
                await ReplyAsync("You must be connected to a voice channel!");
                return;
            }
            
            try {
                player = await lavaNode.JoinAsync(voiceState.VoiceChannel);
                await ReplyAsync($"Joined {voiceState.VoiceChannel.Name}!");
                audioService.TextChannels.TryAdd(Context.Guild.Id, Context.Channel.Id);
            }
            catch (Exception exception) {
                await ReplyAsync(exception.Message);
            }
        }
        
        var searchResponse = await lavaNode.LoadTrackAsync(searchQuery);
        if (searchResponse.Type is SearchType.Empty or SearchType.Error) {
            await ReplyAsync($"I wasn't able to find anything for `{searchQuery}`.");
            return;
        }

        var track = searchResponse.Tracks.FirstOrDefault();
        if (player.GetQueue().Count == 0) {
            await player.PlayAsync(lavaNode, track);
            await ReplyAsync($"Now playing: {track.Title}");
            return;
        }
        
        player.GetQueue().Enqueue(track);
        await ReplyAsync($"Added {track.Title} to queue.");
    }

    [SlashCommand("stop", "Stops the current song and clears the queue."), RequirePlayer]
    public async Task StopAsync() {
        var player = await _lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected || player.Track == null) {
            await ReplyAsync("Woah, can't stop won't stop.");
            return;
        }
        
        try {
            await player.StopAsync(_lavaNode, player.Track);
            await ReplyAsync("No longer playing anything.");
        }
        catch (Exception exception) {
            await ReplyAsync(exception.Message);
        }
    }
    
    
}