using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using ThornBot.Attributes;
using ThornBot.Handlers;
using ThornBot.Services;

namespace ThornBot.Modules;

// The moderation suite. Every command is guarded by RequireGuildPermission
// (Discord-level authority) and additionally enforces role-hierarchy rules so
// a moderator can never act on someone above them or on the server owner —
// the same checks Discord's own UI applies. Every action is written to the
// audit channel via AuditLogService.
[DefaultMemberPermissions(GuildPermission.ManageMessages)]
public class ModerationModule(AuditLogService audit, WarnService warns)
    : InteractionModuleBase<SocketInteractionContext>
{
    // --- Kick ---
    [SlashCommand("kick", "Kick a member from the server."), RequireGuildPermission(GuildPermission.KickMembers)]
    public async Task KickAsync(
        [Summary("user", "The member to kick")] IGuildUser user,
        [Summary("reason", "Reason for the kick")] string? reason = null)
    {
        if (!await CanModerateAsync(user, "kick")) return;

        try
        {
            if (reason is null)
                await user.KickAsync();
            else
                await user.KickAsync(reason);

            await audit.LogAsync(Context.Guild.Id, "Kick", Context.User, user, reason);
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "👢 Kicked", $"{user.Username} was kicked.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Ban ---
    [SlashCommand("ban", "Ban a member from the server."), RequireGuildPermission(GuildPermission.BanMembers)]
    public async Task BanAsync(
        [Summary("user", "The member to ban")] IGuildUser user,
        [Summary("delete_days", "Days of message history to delete (0-7)"), MinValue(0), MaxValue(7)] int deleteDays = 0,
        [Summary("reason", "Reason for the ban")] string? reason = null)
    {
        if (!await CanModerateAsync(user, "ban")) return;

        try
        {
            await Context.Guild.AddBanAsync(user, deleteDays, reason);
            await audit.LogAsync(Context.Guild.Id, "Ban", Context.User, user, reason,
                deleteDays > 0 ? $"Deleted {deleteDays} day(s) of messages." : null);
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🔨 Banned", $"{user.Username} was banned.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Unban ---
    [SlashCommand("unban", "Remove a ban for a user ID."), RequireGuildPermission(GuildPermission.BanMembers)]
    public async Task UnbanAsync(
        [Summary("user_id", "The ID of the banned user")] string userId,
        [Summary("reason", "Reason for the unban")] string? reason = null)
    {
        if (!ulong.TryParse(userId, out var uid))
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("That's not a valid user ID."), ephemeral: true);
            return;
        }

        try
        {
            await Context.Guild.RemoveBanAsync(uid);
            await audit.LogAsync(Context.Guild.Id, "Unban", Context.User, null, reason, $"User ID `{uid}`");
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🕊️ Unbanned", $"`{uid}` was unbanned.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed($"Could not unban: {ex.Message}"), ephemeral: true);
        }
    }

    // --- Timeout ---
    [SlashCommand("timeout", "Timeout (mute) a member for a duration."), RequireGuildPermission(GuildPermission.ModerateMembers)]
    public async Task TimeoutAsync(
        [Summary("user", "The member to timeout")] IGuildUser user,
        [Summary("minutes", "Duration in minutes (max 10080 = 7 days)"), MinValue(1), MaxValue(10080)] int minutes,
        [Summary("reason", "Reason for the timeout")] string? reason = null)
    {
        if (!await CanModerateAsync(user, "timeout")) return;

        if (user.TimedOutUntil is not null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("That user is already timed out."), ephemeral: true);
            return;
        }

        try
        {
            await user.SetTimeOutAsync(TimeSpan.FromMinutes(minutes), new RequestOptions { AuditLogReason = reason });
            await audit.LogAsync(Context.Guild.Id, "Timeout", Context.User, user, reason,
                $"Duration: {minutes} minute(s).");
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🤐 Timed out", $"{user.Username} was timed out for {minutes} minute(s).", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Remove timeout ---
    [SlashCommand("untimeout", "Remove a timeout from a member."), RequireGuildPermission(GuildPermission.ModerateMembers)]
    public async Task UntimeoutAsync(
        [Summary("user", "The member to remove the timeout from")] IGuildUser user,
        [Summary("reason", "Reason for removing the timeout")] string? reason = null)
    {
        if (user.TimedOutUntil is null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("That user is not timed out."), ephemeral: true);
            return;
        }

        try
        {
            await user.RemoveTimeOutAsync(new RequestOptions { AuditLogReason = reason });
            await audit.LogAsync(Context.Guild.Id, "Untimeout", Context.User, user, reason);
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🔓 Timeout removed", $"{user.Username}'s timeout was removed.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Purge ---
    [SlashCommand("purge", "Bulk-delete recent messages in this channel."), RequireGuildPermission(GuildPermission.ManageMessages)]
    public async Task PurgeAsync(
        [Summary("count", "Number of messages to delete (1-100)"), MinValue(1), MaxValue(100)] int count,
        [Summary("user", "Only delete messages from this user")] IUser? user = null)
    {
        if (Context.Channel is not ITextChannel textChannel)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("This command must be run in a text channel."), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        try
        {
            var deleted = 0;
            var remaining = count;
            while (remaining > 0)
            {
                var batch = await textChannel.GetMessagesAsync(Math.Min(remaining + 1, 100)).FlattenAsync();
                var toDelete = user is null
                    ? batch.Where(m => !m.IsPinned).Take(remaining).ToList()
                    : batch.Where(m => m.Author.Id == user.Id && !m.IsPinned).Take(remaining).ToList();

                if (toDelete.Count == 0)
                    break;

                if (toDelete.Count == 1)
                    await textChannel.DeleteMessageAsync(toDelete[0]);
                else
                    await textChannel.DeleteMessagesAsync(toDelete);

                deleted += toDelete.Count;
                remaining -= toDelete.Count;
            }

            await audit.LogAsync(Context.Guild.Id, "Purge", Context.User, user, null, $"{deleted} message(s) deleted from #{textChannel.Name}.");
            await FollowupAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🧹 Purged", $"Deleted **{deleted}** message(s).", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await FollowupAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Warn ---
    [SlashCommand("warn", "Issue a warning to a member."), RequireGuildPermission(GuildPermission.ManageMessages)]
    public async Task WarnAsync(
        [Summary("user", "The member to warn")] IGuildUser user,
        [Summary("reason", "Reason for the warning")] string reason)
    {
        if (!await CanModerateAsync(user, "warn")) return;

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 400)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("Please provide a reason under 400 characters."), ephemeral: true);
            return;
        }

        var caseId = warns.AddWarn(Context.Guild.Id, user.Id, Context.User.Id, reason);
        var count = warns.WarnCount(Context.Guild.Id, user.Id);

        await audit.LogAsync(Context.Guild.Id, "Warn", Context.User, user, reason,
            $"Case `{caseId}` — {user.Username} now has **{count}** warning(s).");
        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
            "⚠️ Warned", $"{user.Username} was warned. **warnings:** {count}\nCase: `{caseId}`", EmbedColors.Success), ephemeral: true);
    }

    [SlashCommand("warns", "Show a member's warnings."), RequireGuildPermission(GuildPermission.ManageMessages)]
    public async Task WarnsAsync(
        [Summary("user", "The member to look up")] IUser user,
        [Summary("limit", "Max warnings to show (default 10)"), MinValue(1), MaxValue(100)] int limit = 10)
    {
        var records = warns.GetWarns(Context.Guild.Id, user.Id).Take(limit).ToList();
        if (records.Count == 0)
        {
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "📋 Warnings", $"{user.Username} has no warnings."), ephemeral: true);
            return;
        }

        var lines = records.Select(w =>
            $"`{w.CaseId}` — {w.Timestamp:yyyy-MM-dd HH:mm} UTC — {w.Reason}\n" +
            $"     ↳ by <@{w.ModeratorId}>");
        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
            $"📋 Warnings for {user.Username}", string.Join('\n', lines)), ephemeral: true);
    }

    [SlashCommand("removewarn", "Remove a warning by case ID."), RequireGuildPermission(GuildPermission.ManageMessages)]
    public async Task RemoveWarnAsync(
        [Summary("case_id", "The warning case ID to remove")] string caseId)
    {
        if (!warns.RemoveWarn(caseId))
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("No warning found with that case ID."), ephemeral: true);
            return;
        }

        await audit.LogAsync(Context.Guild.Id, "RemoveWarn", Context.User, null, null, $"Case `{caseId}` removed.");
        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
            "🗑️ Warning removed", $"Case `{caseId}` was removed.", EmbedColors.Success), ephemeral: true);
    }

    // --- Lock / Unlock ---
    [SlashCommand("lock", "Lock a channel (deny @everyone Send Messages)."), RequireGuildPermission(GuildPermission.ManageChannels)]
    public async Task LockAsync(
        [Summary("channel", "Channel to lock (defaults to current)")] ITextChannel? channel = null)
    {
        channel ??= Context.Channel as ITextChannel;
        if (channel is null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("This command must be run in a text channel."), ephemeral: true);
            return;
        }

        try
        {
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Deny));
            await audit.LogAsync(Context.Guild.Id, "Lock", Context.User, null, null, $"Locked #{channel.Name}.");
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🔒 Locked", $"#{channel.Name} is now locked.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("unlock", "Unlock a channel (restore @everyone Send Messages)."), RequireGuildPermission(GuildPermission.ManageChannels)]
    public async Task UnlockAsync(
        [Summary("channel", "Channel to unlock (defaults to current)")] ITextChannel? channel = null)
    {
        channel ??= Context.Channel as ITextChannel;
        if (channel is null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("This command must be run in a text channel."), ephemeral: true);
            return;
        }

        try
        {
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(sendMessages: PermValue.Inherit));
            await audit.LogAsync(Context.Guild.Id, "Unlock", Context.User, null, null, $"Unlocked #{channel.Name}.");
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🔓 Unlocked", $"#{channel.Name} is now unlocked.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Slowmode ---
    [SlashCommand("slowmode", "Set the slowmode for a channel."), RequireGuildPermission(GuildPermission.ManageChannels)]
    public async Task SlowmodeAsync(
        [Summary("seconds", "Slowmode delay in seconds (0 to disable), max 21600"), MinValue(0), MaxValue(21600)] int seconds,
        [Summary("channel", "Channel to apply it to (defaults to current)")] ITextChannel? channel = null)
    {
        channel ??= Context.Channel as ITextChannel;
        if (channel is null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("This command must be run in a text channel."), ephemeral: true);
            return;
        }

        try
        {
            await channel.ModifyAsync(c => c.SlowModeInterval = seconds);
            await audit.LogAsync(Context.Guild.Id, "Slowmode", Context.User, null, null,
                $"{seconds}s slowmode on #{channel.Name}.");
            var msg = seconds == 0
                ? $"Slowmode disabled on #{channel.Name}."
                : $"Slowmode set to **{seconds}s** on #{channel.Name}.";
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed("⏳ Slowmode", msg, EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // --- Mute (role-based) ---
    [SlashCommand("mute", "Mute a member via a 'Muted' role."), RequireGuildPermission(GuildPermission.ManageRoles)]
    public async Task MuteAsync(
        [Summary("user", "The member to mute")] IGuildUser user,
        [Summary("reason", "Reason for the mute")] string? reason = null)
    {
        if (!await CanModerateAsync(user, "mute")) return;

        var mutedRole = await GetOrCreateMutedRoleAsync();
        if (mutedRole is null)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("Could not find or create a 'Muted' role."), ephemeral: true);
            return;
        }

        if (user.RoleIds.Contains(mutedRole.Id))
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed($"{user.Username} is already muted."), ephemeral: true);
            return;
        }

        try
        {
            await user.AddRoleAsync(mutedRole);
            await audit.LogAsync(Context.Guild.Id, "Mute", Context.User, user, reason, "Role-based mute applied.");
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🔇 Muted", $"{user.Username} was muted.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("unmute", "Remove the mute role from a member."), RequireGuildPermission(GuildPermission.ManageRoles)]
    public async Task UnmuteAsync(
        [Summary("user", "The member to unmute")] IGuildUser user,
        [Summary("reason", "Reason for the unmute")] string? reason = null)
    {
        var mutedRole = Context.Guild.Roles.FirstOrDefault(r => r.Name == "Muted");
        if (mutedRole is null || !user.RoleIds.Contains(mutedRole.Id))
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed($"{user.Username} is not muted."), ephemeral: true);
            return;
        }

        try
        {
            await user.RemoveRoleAsync(mutedRole);
            await audit.LogAsync(Context.Guild.Id, "Unmute", Context.User, user, reason);
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                "🔊 Unmuted", $"{user.Username} was unmuted.", EmbedColors.Success), ephemeral: true);
        }
        catch (Exception ex)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(ex.Message), ephemeral: true);
        }
    }

    // Attempts to find an existing role literally named "Muted"; if missing,
    // creates one that denies Send Messages / Add Reactions in every text
    // channel and Speak in every voice channel. Returns null if it couldn't.
    private async Task<IRole?> GetOrCreateMutedRoleAsync()
    {
        var existing = Context.Guild.Roles.FirstOrDefault(r => r.Name == "Muted");
        if (existing is not null)
            return existing;

        try
        {
            var role = await Context.Guild.CreateRoleAsync("Muted", GuildPermissions.None);
            foreach (var channel in Context.Guild.Channels)
            {
                if (channel is ITextChannel text)
                {
                    await text.AddPermissionOverwriteAsync(role, new OverwritePermissions(
                        sendMessages: PermValue.Deny, addReactions: PermValue.Deny));
                }
                else if (channel is IVoiceChannel voice)
                {
                    await voice.AddPermissionOverwriteAsync(role, new OverwritePermissions(speak: PermValue.Deny));
                }
            }
            return role;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Could not create Muted role: {ex.Message}");
            return null;
        }
    }

    // Enforces Discord's role-hierarchy rules the same way the Discord client
    // does: you cannot moderate yourself, the server owner, or anyone whose
    // highest role sits at or above yours (and the bot can't act on someone
    // at or above its own highest role either). Uses each member's resolved
    // Hierarchy (highest role position + owner bonus), which Discord.Net
    // computes for us.
    private async Task<bool> CanModerateAsync(IGuildUser target, string _)
    {
        if (target.Id == Context.User.Id)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("You cannot moderate yourself."), ephemeral: true);
            return false;
        }

        if (target.Id == Context.Guild.OwnerId)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed("You cannot moderate the server owner."), ephemeral: true);
            return false;
        }

        var moderator = Context.User as SocketGuildUser;
        var bot = Context.Guild.CurrentUser;

        var modHierarchy = moderator?.Hierarchy ?? 0;
        var botHierarchy = bot.Hierarchy;
        var targetHierarchy = target.Hierarchy;

        if (modHierarchy == 0 || targetHierarchy >= modHierarchy)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(
                "You cannot use that on someone with an equal or higher role than you."), ephemeral: true);
            return false;
        }

        if (targetHierarchy >= botHierarchy)
        {
            await RespondAsync(embed: await EmbedHandler.CreateErrorEmbed(
                "I cannot moderate someone at or above my highest role."), ephemeral: true);
            return false;
        }

        return true;
    }
}
