using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ThornBot.Handlers;
using Victoria;
using Victoria.Enums;
using Victoria.WebSocket.EventArgs;

namespace ThornBot.Services;


public sealed class AudioService
{
    private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
    private readonly DiscordSocketClient _socketClient;
    private readonly RadioService _radioService;
    private readonly ILogger _logger;
    public readonly HashSet<ulong> VoteQueue;
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _disconnectTokens;
    public readonly ConcurrentDictionary<ulong, ulong> TextChannels;
    private readonly ConcurrentDictionary<string, IUser> _requesters;
    private readonly ConcurrentDictionary<ulong, IUserMessage> _nowPlayingMessages;

    public AudioService(
        LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
        DiscordSocketClient socketClient,
        RadioService radioService,
        ILogger<AudioService> logger)
    {
        _lavaNode = lavaNode;
        _socketClient = socketClient;
        _radioService = radioService;
        _disconnectTokens = new ConcurrentDictionary<ulong, CancellationTokenSource>();
        _logger = logger;
        TextChannels = new ConcurrentDictionary<ulong, ulong>();
        _requesters = new ConcurrentDictionary<string, IUser>();
        _nowPlayingMessages = new ConcurrentDictionary<ulong, IUserMessage>();
        VoteQueue = [];
        _lavaNode.OnWebSocketClosed += OnWebSocketClosedAsync;
        _lavaNode.OnStats += OnStatsAsync;
        _lavaNode.OnPlayerUpdate += OnPlayerUpdateAsync;
        _lavaNode.OnTrackEnd += OnTrackEndAsync;
        _lavaNode.OnTrackStart += OnTrackStartAsync;
    }

    public void SetRequester(LavaTrack track, IUser user) => _requesters[track.Hash] = user;

    public IUser? GetRequester(LavaTrack track) =>
        _requesters.TryGetValue(track.Hash, out var user) ? user : null;

    public void ClearRequester(LavaTrack track) => _requesters.TryRemove(track.Hash, out _);

    // Called by /stop (the button already edits the message itself when it
    // handles the click). Safe to call before or after actually stopping the
    // player — idempotent with the Stopped-reason cleanup in OnTrackEndAsync.
    public Task AnnounceStoppedAsync(ulong guildId, string stoppedBy) =>
        FinalizeNowPlayingAsync(guildId, "⏹️ Stopped", $"Playback stopped by {stoppedBy}.");

    // Called by /skip when there's nothing left in the queue to skip to.
    public Task AnnounceQueueFinishedAsync(ulong guildId) =>
        FinalizeNowPlayingAsync(guildId, "⏹️ Queue Finished", "That was the last track — nothing left to play.");

    private async Task OnTrackStartAsync(TrackStartEventArg arg)
    {
        // The radio owns this guild's player while it's live — that track isn't
        // something a user queued, so it doesn't get a player-controls card.
        if (arg.GuildId == _radioService.GuildId && _radioService.IsLive)
            return;

        if (!TextChannels.TryGetValue(arg.GuildId, out var textChannelId))
            return;

        if (_socketClient.GetGuild(arg.GuildId)?.GetChannel(textChannelId) is not ITextChannel channel)
            return;

        var embed = await EmbedHandler.CreateTrackEmbed("Now Playing", "🎶", arg.Track, GetRequester(arg.Track));
        var components = ComponentHandler.CreatePlayerControls(isPaused: false);

        // One message per guild, edited per track, instead of a fresh message
        // (and channel spam) every time the song changes.
        if (_nowPlayingMessages.TryGetValue(arg.GuildId, out var existing))
        {
            try
            {
                await existing.ModifyAsync(m =>
                {
                    m.Embed = embed;
                    m.Components = components;
                });
                return;
            }
            catch
            {
                // Message was deleted out from under us — fall through and send a new one.
                _nowPlayingMessages.TryRemove(arg.GuildId, out _);
            }
        }

        var message = await channel.SendMessageAsync(embed: embed, components: components);
        _nowPlayingMessages[arg.GuildId] = message;
    }

    private async Task OnTrackEndAsync(TrackEndEventArg arg)
    {
        ClearRequester(arg.Track);
        _logger.LogInformation("{Title} ended with reason: {Reason}", arg.Track.Title, arg.Reason);

        switch (arg.Reason)
        {
            // Victoria/LavaLink don't auto-advance the queue on their own — without
            // this, only the first track of a queue ever plays.
            case TrackEndReason.Finished or TrackEndReason.Load_Failed:
                var player = await _lavaNode.TryGetPlayerAsync(arg.GuildId);
                if (player is not null && player.GetQueue().TryDequeue(out var next))
                {
                    await player.PlayAsync(_lavaNode, next);
                }
                else
                {
                    await FinalizeNowPlayingAsync(arg.GuildId, "⏹️ Queue Finished", "Nothing left to play.");
                }
                break;

            // Stop/skip-button paths already set the message to its final state
            // themselves — just stop tracking it so the next /play starts fresh.
            case TrackEndReason.Stopped or TrackEndReason.Cleanup:
                _nowPlayingMessages.TryRemove(arg.GuildId, out _);
                break;
        }
    }

    private async Task FinalizeNowPlayingAsync(ulong guildId, string title, string description)
    {
        if (!_nowPlayingMessages.TryRemove(guildId, out var message))
            return;

        try
        {
            var embed = await EmbedHandler.CreateBasicEmbed(title, description, EmbedColors.Success);
            await message.ModifyAsync(m =>
            {
                m.Embed = embed;
                m.Components = ComponentHandler.None;
            });
        }
        catch
        {
            // Message was deleted out from under us — nothing to finalize.
        }
    }

    private Task OnPlayerUpdateAsync(PlayerUpdateEventArg arg)
    {
        _logger.LogInformation("Guild latency: {}", arg.Ping);
        return Task.CompletedTask;
    }

    private Task OnStatsAsync(StatsEventArg arg)
    {
        _logger.LogInformation("{}", JsonSerializer.Serialize(arg));
        return Task.CompletedTask;
    }

    private Task OnWebSocketClosedAsync(WebSocketClosedEventArg arg)
    {
        _logger.LogCritical("{}", JsonSerializer.Serialize(arg));
        return Task.CompletedTask;
    }
}
