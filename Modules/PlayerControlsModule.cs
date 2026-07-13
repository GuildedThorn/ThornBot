using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using ThornBot.Handlers;
using ThornBot.Services;
using Victoria;

namespace ThornBot.Modules;

// Handles the Pause/Skip/Stop buttons attached to the "Now Playing" card sent
// by AudioService.OnTrackStartAsync.
public class PlayerControlsModule(LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode, AudioService audioService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("player:pauseresume")]
    public async Task PauseResumeAsync()
    {
        var component = (SocketMessageComponent)Context.Interaction;
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player?.Track is null)
        {
            await component.UpdateAsync(p => p.Components = ComponentHandler.None);
            return;
        }

        var wasPaused = player.IsPaused;
        if (wasPaused)
            await player.ResumeAsync(lavaNode, player.Track);
        else
            await player.PauseAsync(lavaNode);

        var embed = await EmbedHandler.CreateTrackEmbed(
            wasPaused ? "Now Playing" : "Paused",
            wasPaused ? "🎶" : "⏸️",
            player.Track,
            audioService.GetRequester(player.Track));

        await component.UpdateAsync(p =>
        {
            p.Embed = embed;
            p.Components = ComponentHandler.CreatePlayerControls(isPaused: !wasPaused);
        });
    }

    [ComponentInteraction("player:skip")]
    public async Task SkipAsync()
    {
        var component = (SocketMessageComponent)Context.Interaction;
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player?.Track is null)
        {
            await component.UpdateAsync(p => p.Components = ComponentHandler.None);
            return;
        }

        try
        {
            var (skipped, current) = await player.SkipAsync(lavaNode);
            audioService.ClearRequester(skipped);
            var embed = await EmbedHandler.CreateTrackEmbed("Now Playing", "🎶", current, audioService.GetRequester(current));
            await component.UpdateAsync(p =>
            {
                p.Embed = embed;
                p.Components = ComponentHandler.CreatePlayerControls(isPaused: false);
            });
        }
        catch (InvalidOperationException)
        {
            audioService.ClearRequester(player.Track);
            await audioService.StopPlaybackAsync(lavaNode, Context.Guild.Id);
            var embed = await EmbedHandler.CreateBasicEmbed(
                "⏹️ Queue Finished", "That was the last track — nothing left to play.", EmbedColors.Success);
            await component.UpdateAsync(p =>
            {
                p.Embed = embed;
                p.Components = ComponentHandler.None;
            });
        }
    }

    [ComponentInteraction("player:stop")]
    public async Task StopAsync()
    {
        var component = (SocketMessageComponent)Context.Interaction;
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player?.Track is not null)
        {
            audioService.ClearQueue(player);
            audioService.ClearRequester(player.Track);
            await audioService.StopPlaybackAsync(lavaNode, Context.Guild.Id);
        }

        var displayName = Context.User is IGuildUser guildUser ? guildUser.DisplayName : Context.User.Username;
        var embed = await EmbedHandler.CreateBasicEmbed(
            "⏹️ Stopped", $"Playback stopped by {displayName}.", EmbedColors.Success);

        await component.UpdateAsync(p =>
        {
            p.Embed = embed;
            p.Components = ComponentHandler.None;
        });
    }
}
