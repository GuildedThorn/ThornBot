using Discord;

namespace ThornBot.Handlers;

public static class ComponentHandler
{
    public static MessageComponent CreatePlayerControls(bool isPaused) =>
        new ComponentBuilder()
            .WithButton(isPaused ? "Resume" : "Pause", "player:pauseresume", ButtonStyle.Secondary,
                new Emoji(isPaused ? "▶️" : "⏸️"))
            .WithButton("Skip", "player:skip", ButtonStyle.Secondary, new Emoji("⏭️"))
            .WithButton("Stop", "player:stop", ButtonStyle.Danger, new Emoji("⏹️"))
            .Build();

    public static MessageComponent None => new ComponentBuilder().Build();
}
