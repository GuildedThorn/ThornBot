using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using ThornBot.Handlers;
using Victoria;
using Victoria.Rest.Search;

namespace ThornBot.Services;

// Talks to GuildedThorn.com's self-hosted radio relay (RadioService/RadioController),
// which replaced the old standalone Icecast2 server. GET {radioBaseUrl}/api/radio/status
// mirrors what status-json.xsl used to give us; {radioBaseUrl}/api/radio/stream is the
// live audio LavaLink plays directly, same as the old Icecast mount URL.
public class RadioService(
    DiscordSocketClient client,
    string radioBaseUrl,
    ulong notifyChannelId,
    ulong voiceChannelId,
    ulong guildId,
    IServiceProvider services)
{
    private string _lastTitle = "";
    private string _lastArtist = "";
    private bool _wasOnline;
    private readonly HttpClient _http = new();

    // Read by RequireRadioNotLiveAttribute to gate music commands while the radio
    // is broadcasting in this same guild.
    public bool IsLive { get; private set; }
    public ulong GuildId { get; } = guildId;
    public string Name { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";

    public async Task StartMonitoringAsync()
    {
        while (true)
        {
            try
            {
                var response = await _http.GetStringAsync($"{radioBaseUrl}/api/radio/status");
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var isOnline = root.TryGetProperty("online", out var onlineProp) && onlineProp.GetBoolean();
                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                var artist = root.TryGetProperty("artist", out var artistProp) ? artistProp.GetString() ?? "" : "";

                Name = name;

                // Went online
                if (isOnline && !_wasOnline)
                {
                    await SendMessageAsync($"🎵 {(string.IsNullOrWhiteSpace(name) ? "Radio" : name)} is online!");
                    await JoinAndPlayStreamAsync();
                    await PresenceHandler.SetListeningAsync(client, string.IsNullOrWhiteSpace(name) ? "the radio" : name);
                }

                switch (isOnline)
                {
                    // Song/artist changed
                    case true when title != _lastTitle || artist != _lastArtist:
                        await SendMessageAsync($"🎶 Now playing: **{FormatSong(artist, title)}**");
                        break;

                    // Went offline
                    case false when _wasOnline:
                        await SendMessageAsync("❌ Radio went offline!");
                        await LeaveStreamAsync();
                        await PresenceHandler.SetDefaultAsync(client);
                        break;
                }

                _lastTitle = title;
                _lastArtist = artist;
                _wasOnline = isOnline;
                IsLive = isOnline;
                Title = title;
                Artist = artist;
            }
            catch
            {
                if (_wasOnline)
                {
                    await SendMessageAsync("❌ Radio went offline!");
                    await LeaveStreamAsync();
                    await PresenceHandler.SetDefaultAsync(client);
                    _wasOnline = false;
                    IsLive = false;
                }
            }

            await Task.Delay(5000);
        }
    }

    private static string FormatSong(string artist, string title) =>
        string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";

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

        var searchResponse = await lavaNode.LoadTrackAsync($"{radioBaseUrl}/api/radio/stream");
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
