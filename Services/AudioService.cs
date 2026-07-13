using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ThornBot.Handlers;
using Victoria;
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
        await channel.SendMessageAsync(embed: embed, components: ComponentHandler.CreatePlayerControls(isPaused: false));
    }

    private Task OnTrackEndAsync(TrackEndEventArg arg)
    {
        ClearRequester(arg.Track);
        _logger.LogInformation("{Title} ended with reason: {Reason}", arg.Track.Title, arg.Reason);
        return Task.CompletedTask;
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
