using Discord;
using Discord.Interactions;
using ThornBot.Services;

namespace ThornBot.Handlers;

// Backs /radio play's id parameter — lets users pick a past broadcast from a
// dropdown of dates/durations instead of copy-pasting a GUID out of /radio archive.
public class RadioArchiveAutocompleteHandler(RadioService radioService) : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        try
        {
            var (items, _) = await radioService.GetArchiveAsync(page: 1, pageSize: 25);
            var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

            var results = items
                .Select(item => new AutocompleteResult(FormatLabel(item), item.Id))
                .Where(result => typed.Length == 0 || result.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Take(25);

            return AutocompletionResult.FromSuccess(results);
        }
        catch
        {
            // Archive unreachable — let /radio play still accept a manually typed id.
            return AutocompletionResult.FromSuccess([]);
        }
    }

    private static string FormatLabel(ArchiveEntry item)
    {
        var name = string.IsNullOrWhiteSpace(item.StationName) ? "Radio" : item.StationName;
        var duration = EmbedHandler.FormatDuration(TimeSpan.FromSeconds(item.DurationSeconds));
        return $"{item.StartedAt:MMM d, h:mm tt} UTC — {name} ({duration})";
    }
}
