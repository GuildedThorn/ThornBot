using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using ThornBot.Handlers;

namespace ThornBot.Services;

// Centralized moderation audit trail. Every moderation action (kick, ban,
// timeout, purge, warn, unlock, slowmode, ...) is reported to a dedicated
// channel so there's a tamper-resistant-ish, searchable record of who did what
// to whom and why. The bot also writes the JSON of each action to the journal
// as a belt-and-suspenders copy for SIEM shipping.
public class AuditLogService(IConfiguration configuration, DiscordSocketClient client)
{
    public async Task LogAsync(ulong guildId, string action, IUser moderator, IUser? target, string? reason = null, string? detail = null)
    {
        var description = $"**Moderator:** {ModeratorLabel(moderator)}\n" +
                          $"**Action:** {action}";

        if (target is not null)
            description += $"\n**Target:** {ModeratorLabel(target)}";

        if (!string.IsNullOrWhiteSpace(reason))
            description += $"\n**Reason:** {reason}";

        if (!string.IsNullOrWhiteSpace(detail))
            description += $"\n{detail}";

        var embed = await EmbedHandler.CreateBasicEmbed($"🛡️ {action}", description, EmbedColors.Brand);

        if (TryGetAuditChannel(guildId, out var channel))
        {
            try
            {
                await channel.SendMessageAsync(embed: embed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ AuditLogService: failed to post to audit channel: {ex.Message}");
            }
        }

        // Journal copy (still informative reference in the logs).
        Console.WriteLine($"[AUDIT] guild={guildId} action={action} moderator={moderator.Id} " +
                          $"target={target?.Id.ToString() ?? "none"} reason={reason ?? ""}");
    }

    private static string ModeratorLabel(IUser user) =>
        $"{user.Username} (`{user.Id}`)";

    private bool TryGetAuditChannel(ulong guildId, out IMessageChannel channel)
    {
        channel = null!;
        if (!ulong.TryParse(configuration["discord:auditChannelId"], out var auditChannelId) || auditChannelId == 0)
            return false;

        var guild = client.GetGuild(guildId);
        if (guild?.GetChannel(auditChannelId) is IMessageChannel mc)
        {
            channel = mc;
            return true;
        }

        return false;
    }
}
