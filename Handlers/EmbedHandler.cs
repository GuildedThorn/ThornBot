using Discord;

namespace ThornBot.Handlers; 

public static class EmbedHandler {

    public static async Task<Embed> CreateBasicEmbed(string title, string description) {
        var embed = await Task.Run(() => new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(Color.DarkBlue)
            .WithCurrentTimestamp()
            .Build());
        return embed;
    }

    public static async Task<Embed> CreateBasicEmbedWithFields(string title, string description, EmbedFieldBuilder[] fields) {
        var embed = await Task.Run(() => new EmbedBuilder()
            .WithTitle(title)
            .WithFields(fields)
            .WithDescription(description)
            .WithColor(Color.DarkBlue)
            .WithCurrentTimestamp()
            .Build());
        return embed;
    }

    public static async Task<Embed> CreateErrorEmbed(string error) {
        var embed = await Task.Run(() => new EmbedBuilder()
            .WithTitle("ThornBot | error")
            .WithDescription($"Error: {error}")
            .WithColor(Color.Red)
            .WithCurrentTimestamp()
            .Build());
        return embed;
    }
}