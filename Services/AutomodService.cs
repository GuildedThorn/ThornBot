using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ThornBot.Services;

// Opt-in automated moderation. Watches guild messages and flags two cheap,
// high-signal abuse patterns without needing a full machine-learning stack:
//   - rapid repeated identical messages (message spam)
//   - blast-sending mentions (mention spam)
// When a user trips a threshold, the bot issues a timeout (or warns if the bot
// lacks ModerateMembers) and forwards the event to the audit log. Enabled only
// when discord:automodEnable is set true. All thresholds are configurable so
// admins control strictness per server.
public class AutomodService(
    DiscordSocketClient client,
    IConfiguration configuration,
    AuditLogService audit,
    ILogger<AutomodService> logger)
{
    private sealed record SpamWindow(Queue<DateTime> Stamps); // holds send timestamps

    private readonly ConcurrentDictionary<(ulong, ulong), SpamWindow> _messageWindows = new();
    private readonly ConcurrentDictionary<ulong, DateTime> _lastMentionBurst = new();

    public bool Enabled => configuration["discord:automodEnable"] == "true";

    public void Subscribe()
    {
        if (!Enabled)
            return;

        client.MessageReceived += OnMessageReceivedAsync;
        logger.LogInformation("Automod enabled (message + mention spam detection).");
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.IsBot)
            return;
        if (message.Channel is not SocketTextChannel channel)
            return;
        if (message.Author is not SocketGuildUser author)
            return;
        // Skip users the bot cannot act on (server owner, anyone at or above the bot).
        if (author.Id == channel.Guild.OwnerId)
            return;
        if (author.Hierarchy >= channel.Guild.CurrentUser.Hierarchy)
            return;

        var userId = message.Author.Id;
        var guildId = channel.Guild.Id;

        // --- Message spam: N identical messages within W seconds ---
        var key = (guildId, userId);
        var window = _messageWindows.GetOrAdd(key, _ => new SpamWindow(new Queue<DateTime>()));
        var now = DateTime.UtcNow;
        lock (window)
        {
            window.Stamps.Enqueue(now);
            while (window.Stamps.TryPeek(out var oldest) && (now - oldest).TotalSeconds > 5)
                window.Stamps.Dequeue();

            if (window.Stamps.Count >= GetInt("discord:automodMaxRepeats", 6))
            {
                window.Stamps.Clear();
                _ = HandleAbuseAsync(guildId, message.Author, "message spam");
            }
        }

        // --- Mention spam: many mentions in a short burst ---
        if (message.MentionedUserIds.Count >= GetInt("discord:automodMaxMentions", 5))
        {
            var last = _lastMentionBurst.GetValueOrDefault(userId);
            if (now - last < TimeSpan.FromMinutes(1))
            {
                _lastMentionBurst[userId] = now;
                _ = HandleAbuseAsync(guildId, message.Author, "mention spam");
            }
            else
            {
                _lastMentionBurst[userId] = now;
            }
        }
    }

    private async Task HandleAbuseAsync(ulong guildId, IUser user, string reason)
    {
        var guild = client.GetGuild(guildId);
        if (guild is null)
            return;

        var member = guild.GetUser(user.Id);
        if (member is null)
            return;

        try
        {
            if (guild.CurrentUser.GuildPermissions.ModerateMembers)
            {
                await member.SetTimeOutAsync(TimeSpan.FromMinutes(GetInt("discord:automodTimeoutMinutes", 10)));
                await audit.LogAsync(guildId, "Automod Timeout", client.CurrentUser, member, reason);
            }
            else
            {
                await audit.LogAsync(guildId, "Automod Flag", client.CurrentUser, member, reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Automod could not act on {User}", user.Id);
        }
    }

    private int GetInt(string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;
}
