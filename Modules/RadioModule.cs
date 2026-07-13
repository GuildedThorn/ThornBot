using Discord.Interactions;
using ThornBot.Handlers;
using ThornBot.Services;

namespace ThornBot.Modules;

public class RadioModule(RadioService radioService) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("radio", "Shows whether the radio is live and what's playing.")]
    public async Task StatusAsync()
    {
        var name = string.IsNullOrWhiteSpace(radioService.Name) ? "Radio" : radioService.Name;

        if (!radioService.IsLive)
        {
            await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
                $"📻 {name}", "Offline right now."), ephemeral: true);
            return;
        }

        var nowPlaying = string.IsNullOrWhiteSpace(radioService.Artist)
            ? radioService.Title
            : $"{radioService.Artist} - {radioService.Title}";

        await RespondAsync(embed: await EmbedHandler.CreateBasicEmbed(
            $"📻 {name}",
            string.IsNullOrWhiteSpace(nowPlaying)
                ? "Live now!"
                : $"Live now — **{nowPlaying}**"), ephemeral: true);
    }
}
