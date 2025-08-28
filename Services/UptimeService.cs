namespace ThornBot.Services;

public class UptimeService(
    string pushUrl)
{
    private readonly HttpClient _http = new();

    public async Task StartMonitoringAsync()
    {
        while (true)
        {
            try
            {
                await _http.GetAsync(pushUrl);
            }
            catch (Exception ex)
            {
                // Log the error but don't exit the loop
                Console.WriteLine($"❌ Failed to reach the uptime URL: {ex.Message}");
            }

            await Task.Delay(60000); // Wait 60 seconds before checking again
        }
    }
}