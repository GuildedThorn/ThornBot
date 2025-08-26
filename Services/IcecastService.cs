using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Victoria;
using Victoria.Rest.Search;

namespace ThornBot.Services;

public class IcecastService(
    DiscordSocketClient client,
    string icecastUrl,
    ulong notifyChannelId,
    ulong voiceChannelId,
    ulong guildId,
    IServiceProvider services)
{
    private string _lastSong = "";
    private bool _wasOnline = false;
    private readonly HttpClient _http = new();

    public async Task StartMonitoringAsync()
    {
        while (true)
        {
            try
            {
                var response = await _http.GetStringAsync(icecastUrl + "/status-json.xsl");
                using var doc = JsonDocument.Parse(response);

                var source = doc.RootElement
                    .GetProperty("icestats")
                    .GetProperty("source");

                var currentSong = source.TryGetProperty("title", out var titleProp)
                    ? titleProp.GetString()
                    : null;

                var normalizedSong = (currentSong ?? "").Trim();
                var isOnline = !string.IsNullOrEmpty(normalizedSong);

                // Went online
                if (isOnline && !_wasOnline)
                {
                    await SendMessageAsync($"🎵 Stream is online! Now playing: **{normalizedSong}**");
                    await JoinAndPlayStreamAsync();
                }

                switch (isOnline)
                {
                    // Song changed
                    case true when _lastSong != normalizedSong:
                        await SendMessageAsync($"🎶 Now playing: **{normalizedSong}**");
                        break;

                    // Went offline
                    case false when _wasOnline:
                        await SendMessageAsync("❌ Stream went offline!");
                        await LeaveStreamAsync();
                        break;
                }

                _lastSong = normalizedSong;
                _wasOnline = isOnline;
            }
            catch
            {
                if (_wasOnline)
                {
                    await SendMessageAsync("❌ Stream went offline!");
                    await LeaveStreamAsync();
                    _wasOnline = false;
                }
            }

            await Task.Delay(5000);
        }
    }

    private async Task JoinAndPlayStreamAsync()
    {
        var lavaNode = services.GetRequiredService<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>>();
        var guild = client.GetGuild(guildId);
        var channel = guild.GetVoiceChannel(voiceChannelId);

        if (channel is null)
            return;

        var player = await lavaNode.JoinAsync(channel);

        // If it's a stage channel, try moving to speakers
        if (channel is SocketStageChannel stage)
        {
            try
            {
                // Promote bot to speaker (requires Manage Channel perms)
                await stage.BecomeSpeakerAsync();

                // If that fails, fallback to requesting to speak
                if (!stage.Speakers.Any(u => u.Id == client.CurrentUser.Id && !u.IsSuppressed))
                {
                    await stage.RequestToSpeakAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to auto-promote bot to speaker: {ex.Message}");
            }
        }

        var searchResponse = await lavaNode.LoadTrackAsync(icecastUrl);
        if (searchResponse.Type is SearchType.Empty or SearchType.Error)
            return;

        var track = searchResponse.Tracks.FirstOrDefault();
        if (track is not null)
        {
            await player.PlayAsync(lavaNode, track);
        }
    }


    private async Task LeaveStreamAsync()
    {
        var lavaNode = services.GetRequiredService<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>>();
        if (client.GetChannel(voiceChannelId) is not SocketVoiceChannel voiceChannel)
            return;

        var player = await lavaNode.TryGetPlayerAsync(guildId);
        if (player != null && player.State.IsConnected)
        {
            try
            {
                await lavaNode.LeaveAsync(voiceChannel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ {ex.Message}");
            }
        }
    }

    private async Task SendMessageAsync(string message)
    {
        if (client.GetChannel(notifyChannelId) is IMessageChannel channel)
            await channel.SendMessageAsync(message);
    }
}
