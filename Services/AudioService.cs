using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
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
    private readonly HttpClient _lavalinkHttp;
    public readonly HashSet<ulong> VoteQueue;
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _disconnectTokens;
    public readonly ConcurrentDictionary<ulong, ulong> TextChannels;
    private readonly ConcurrentDictionary<string, IUser> _requesters;
    private readonly ConcurrentDictionary<ulong, IUserMessage> _nowPlayingMessages;

    public AudioService(
        LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
        DiscordSocketClient socketClient,
        RadioService radioService,
        IConfiguration configuration,
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

        // Victoria's own UpdatePlayerAsync always serializes with
        // JsonIgnoreCondition.WhenWritingDefault, which silently strips ANY field
        // equal to its default — including an intentional EncodedTrack: null,
        // since null IS string's default. That means Victoria itself can never put
        // the literal `"encodedTrack": null` on the wire that Lavalink needs to
        // actually stop a track; going through it always produces an effectively
        // empty PATCH body, which Lavalink treats as "no change." StopPlaybackAsync
        // below issues that request by hand instead, same host/port/auth as the
        // AddLavaNode config in ThornBot.cs.
        var hostname = configuration["lavalink:hostname"] ?? "localhost";
        var port = configuration["lavalink:port"] is not null ? ushort.Parse(configuration["lavalink:port"]!) : (ushort)2333;
        var authorization = configuration["lavalink:authorization"] ?? "youshallnotpass";
        _lavalinkHttp = new HttpClient { BaseAddress = new Uri($"http://{hostname}:{port}") };
        _lavalinkHttp.DefaultRequestHeaders.Add("Authorization", authorization);

        _lavaNode.OnWebSocketClosed += OnWebSocketClosedAsync;
        _lavaNode.OnStats += OnStatsAsync;
        _lavaNode.OnPlayerUpdate += OnPlayerUpdateAsync;
        _lavaNode.OnTrackEnd += OnTrackEndAsync;
        _lavaNode.OnTrackStart += OnTrackStartAsync;
        _socketClient.UserVoiceStateUpdated += OnUserVoiceStateUpdatedAsync;
    }

    public void SetRequester(LavaTrack track, IUser user) => _requesters[track.Hash] = user;

    public IUser? GetRequester(LavaTrack track) =>
        _requesters.TryGetValue(track.Hash, out var user) ? user : null;

    public void ClearRequester(LavaTrack track) => _requesters.TryRemove(track.Hash, out _);

    // player.GetQueue() only reflects tracks waiting behind the current one —
    // checking it alone would replace whatever's actively playing right now
    // instead of queueing behind it. Shared by /play and archive playback so
    // that decision only lives in one place.
    public async Task<bool> PlayOrEnqueueAsync(LavaPlayer<LavaTrack> player,
        LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, LavaTrack track, IUser requestedBy)
    {
        SetRequester(track, requestedBy);

        if (player.Track is null)
        {
            await player.PlayAsync(lavaNode, track);
            return true;
        }

        player.GetQueue().Enqueue(track);
        return false;
    }

    // Victoria's own LavaPlayer.StopAsync() extension is broken: it resends the
    // CURRENTLY PLAYING track's own encoded hash instead of null, so Lavalink just
    // pauses the same still-loaded track rather than clearing it. No real TrackEnd
    // event ever fires, and player.Track stays stuck non-null forever. Victoria's
    // UpdatePlayerAsync can't fix this either — its serializer strips the null
    // we'd want to send (see the constructor comment) — so this bypasses Victoria
    // entirely and PATCHes Lavalink's REST API by hand with a literal JSON null,
    // which is what actually stops playback.
    public async Task StopPlaybackAsync(LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, ulong guildId)
    {
        using var body = new StringContent("{\"encodedTrack\":null}", Encoding.UTF8, "application/json");
        using var response = await _lavalinkHttp.PatchAsync(
            $"/v4/sessions/{lavaNode.SessionId}/players/{guildId}?noReplace=false", body);
        response.EnsureSuccessStatusCode();
    }

    // Clears both the queue and the requester tracking for whatever's still in it —
    // shared by every stop path (slash command, button, idle auto-disconnect) so
    // "stop" consistently means "stop and forget everything queued."
    public void ClearQueue(LavaPlayer<LavaTrack> player)
    {
        var queue = player.GetQueue();
        foreach (var queuedTrack in queue)
            ClearRequester(queuedTrack);
        queue.Clear();
    }

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

        await PresenceHandler.SetListeningAsync(_socketClient, arg.Track.Title);

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
        // Presence is global, not per-guild — don't clobber the radio's
        // presence with the idle default if it's live (possibly in another
        // guild entirely).
        if (!_radioService.IsLive)
            await PresenceHandler.SetDefaultAsync(_socketClient);

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

    private const int IdleDisconnectMinutes = 3;

    // Leave voice after everyone else has left, instead of sitting connected
    // (and, if something's still queued, playing to an empty room) forever.
    private Task OnUserVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
    {
        var guild = oldState.VoiceChannel?.Guild ?? newState.VoiceChannel?.Guild;
        if (guild is null)
            return Task.CompletedTask;

        // The radio's dedicated channel stays joined regardless of listeners —
        // it's ambient, not something anyone actively started.
        if (guild.Id == _radioService.GuildId && _radioService.IsLive)
            return Task.CompletedTask;

        var botChannel = guild.CurrentUser?.VoiceChannel;
        if (botChannel is null)
        {
            CancelDisconnectTimer(guild.Id);
            return Task.CompletedTask;
        }

        // Only reconsider when the change actually touches the bot's own channel.
        if (oldState.VoiceChannel?.Id != botChannel.Id && newState.VoiceChannel?.Id != botChannel.Id)
            return Task.CompletedTask;

        if (botChannel.Users.Count(u => !u.IsBot) == 0)
            ScheduleDisconnect(guild.Id, botChannel);
        else
            CancelDisconnectTimer(guild.Id);

        return Task.CompletedTask;
    }

    private void CancelDisconnectTimer(ulong guildId)
    {
        if (_disconnectTokens.TryRemove(guildId, out var cts))
            cts.Cancel();
    }

    private void ScheduleDisconnect(ulong guildId, SocketVoiceChannel channel)
    {
        CancelDisconnectTimer(guildId);
        var cts = new CancellationTokenSource();
        _disconnectTokens[guildId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(IdleDisconnectMinutes), cts.Token);
            }
            catch (TaskCanceledException)
            {
                return; // someone rejoined before the timer elapsed
            }

            _disconnectTokens.TryRemove(guildId, out _);

            if (channel.Users.Count(u => !u.IsBot) > 0)
                return; // defensive re-check in case an update was missed

            var player = await _lavaNode.TryGetPlayerAsync(guildId);
            if (player?.Track is not null)
            {
                ClearQueue(player);
                await FinalizeNowPlayingAsync(guildId, "⏹️ Left", "Nobody was listening, so I left the channel.");
                await StopPlaybackAsync(_lavaNode, guildId);
            }

            try
            {
                await _lavaNode.LeaveAsync(channel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Idle auto-disconnect failed to leave {ChannelName}", channel.Name);
            }
        }, cts.Token);
    }
}
