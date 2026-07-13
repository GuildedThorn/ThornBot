using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using ThornBot.Services;

namespace ThornBot.Attributes;

// Blocks music commands while the radio is broadcasting in its dedicated guild —
// LavaLink only holds one voice connection per guild, and the radio auto-join owns
// it for the duration of the stream. Other guilds' music commands are unaffected.
public class RequireRadioNotLiveAttribute : PreconditionAttribute {
    public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services) {
        var radio = services.GetRequiredService<RadioService>();

        if (context.Guild?.Id == radio.GuildId && radio.IsLive) {
            return Task.FromResult(PreconditionResult.FromError(
                "📻 Radio is live right now — music commands are disabled until it ends."));
        }

        return Task.FromResult(PreconditionResult.FromSuccess());
    }
}
