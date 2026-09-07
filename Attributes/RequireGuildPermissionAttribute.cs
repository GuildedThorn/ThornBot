using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ThornBot.Attributes;

// Guard for guild-command permissions. Confirms the interaction is a guild
// interaction (never a DM), then checks the calling user holds the given guild
// permission. Discord's built-in DefaultMemberPermissions only gates the slash
// command's visibility/registration for non-admin members; it does NOT stop an
// admin-only command from being invoked in a context where the check matters,
// and it gives no per-call enforcement. This attribute is the authoritative
// per-invocation gate used by every moderation command.
public sealed class RequireGuildPermissionAttribute(GuildPermission permission) : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {
        if (context.Guild == null)
            return Task.FromResult(PreconditionResult.FromError("This command can only be used in a server."));

        if (context.User is not IGuildUser guildUser)
            return Task.FromResult(PreconditionResult.FromError("Could not resolve you as a server member."));

        return Task.FromResult(guildUser.GuildPermissions.Has(permission)
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError($"You need the `{permission}` permission to use this command."));
    }
}

// Gates a command behind the bot's owner, resolved from configuration
// (Discord:ownerId). More reliable than trusting context.Client.Application,
// which is only populated after a full application-info round trip. The owner
// is the single most-trusted identity — reserved for destructive or
// configuration-level commands that even a guild administrator shouldn't run.
public sealed class RequireOwnerAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services)
    {
        if (context.User.Id == context.Client.CurrentUser.Id)
            return Task.FromResult(PreconditionResult.FromError("The bot cannot act on itself."));

        var config = services.GetRequiredService<IConfiguration>();
        if (!ulong.TryParse(config["discord:ownerId"], out var ownerId) || ownerId == 0)
            return Task.FromResult(PreconditionResult.FromError("Bot owner is not configured."));

        return Task.FromResult(context.User.Id == ownerId
            ? PreconditionResult.FromSuccess()
            : PreconditionResult.FromError("This command is restricted to the bot owner."));
    }
}
