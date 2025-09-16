using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Renci.SshNet;
using Serilog;
using ThornBot.Handlers;

namespace ThornBot.Services;

public class TrueNasService(IConfiguration configuration, DiscordSocketClient discordClient) {
    
    private IUserMessage? _statusMessage;
    
    public void StartMonitoring() {
        var client = new SshClient(
            configuration["truenas:Host"] ?? "localhost",
            configuration["truenas:Username"] ?? "root",
            configuration["truenas:Password"] ?? "IamAPassword"
        );
        client.Connect();

        if (!client.IsConnected) {
            client = new SshClient(
                configuration["truenas:Host"] ?? "localhost",
                configuration["truenas:Username"] ?? "root",
                new PrivateKeyFile(configuration["truenas:privateKeyPath"] ?? "/path/to/private/key")
            );
            client.Connect();
        }

        if (client.IsConnected)
            Log.Information("✅ [TrueNasService] SSH connected successfully!");
        else
            Log.Error("❌ [TrueNasService] SSH failed to connect");

        // Run loops in background tasks
        _ = Task.Run(() => CheckDriveHealthLoop(client));
    }

    private async Task CheckDriveHealthLoop(SshClient client) {
        while (true) {
            var cmd = client.RunCommand("/sbin/zpool status");
            var result = cmd.Result;

            var guild = discordClient.GetGuild(ulong.Parse(configuration["truenas:LogGuildId"] ?? "0"));
            var channel = guild?.GetTextChannel(ulong.Parse(configuration["truenas:LogChannelId"] ?? "0"));
            if (channel == null) return;
            
            _statusMessage = null;

            var lines = result.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .ToArray();

            var fields = new List<EmbedFieldBuilder>();
            string? currentPool = null;
            var inConfig = false;

            for (var i = 0; i < lines.Length; i++) {
                if (lines[i].StartsWith("pool:")) {
                    currentPool = lines[i].Split(' ')[1];
                    var state = lines[i + 1].Split(':', 2)[1].Trim();
                    var poolEmoji = state.Equals("ONLINE", StringComparison.OrdinalIgnoreCase) ? "✅" : "⚠️";

                    fields.Add(new EmbedFieldBuilder()
                        .WithName($"{poolEmoji} Pool: {currentPool}")
                        .WithValue($"State: {state}")
                        .WithIsInline(false));

                    inConfig = false;
                    continue;
                }

                if (lines[i].StartsWith("config:")) {
                    inConfig = true;
                    i += 1; // skip header line under "config"
                    continue;
                }

                if (inConfig && currentPool != null) {
                    if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("errors:")) continue;

                    var parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var name = parts[0];
                    var state = parts[1];
                    var emoji = state.Equals("ONLINE", StringComparison.OrdinalIgnoreCase) ? "✅" : "⚠️";

                    // Skip the line if it's the same as the pool name to avoid duplicates
                    if (name == currentPool) continue;

                    // Indent mirrors and disks
                    var displayName = name.StartsWith("mirror") || name.StartsWith("raidz")
                        ? $" {emoji} {name}"
                        : $"  {emoji} {name}";

                    fields.Add(new EmbedFieldBuilder()
                        .WithName(displayName)
                        .WithValue($"State: {state}")
                        .WithIsInline(false));
                }

                if (!lines[i].StartsWith("errors:")) continue;
                var errors = lines[i + 1].Trim();
                fields.Add(new EmbedFieldBuilder()
                    .WithName("⚠️ Errors")
                    .WithValue(errors)
                    .WithIsInline(false));
            }


            if (fields.Count > 0)
            {
                var embed = await EmbedHandler.CreateBasicEmbedWithFields(
                    "ThornNas | ZFS Pool Status",
                    "Current status of all ZFS pools, mirrors, and disks:",
                    fields.ToArray()
                );

                if (_statusMessage == null)
                {
                    // Send the initial message and store it
                    _statusMessage = await channel.SendMessageAsync(embed: embed);
                }
                else
                {
                    // Edit the existing message
                    await _statusMessage.ModifyAsync(msg => msg.Embed = embed);
                }
            }
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }
}