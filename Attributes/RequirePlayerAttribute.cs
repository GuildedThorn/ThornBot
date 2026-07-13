using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Victoria;

namespace ThornBot.Attributes;

public class RequirePlayerAttribute : PreconditionAttribute {
    public override async Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context,
        ICommandInfo commandInfo,
        IServiceProvider services) {
        var lavaNode = services.GetRequiredService<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>>();
        var player = await lavaNode.TryGetPlayerAsync(context.Guild.Id);
        return player == null
            ? PreconditionResult.FromError("I'm not connected to a voice channel.")
            : PreconditionResult.FromSuccess();
    }
}
