using System.Collections.Concurrent;
using System.Text.Json;

namespace ThornBot.Services;

public record WarnRecord(ulong GuildId, ulong UserId, ulong ModeratorId, string Reason, DateTime Timestamp, string CaseId);

// Persistent warning store for the moderation suite. Warnings are kept in
// memory for fast lookup and mirrored to a JSON file (path configurable;
// defaults to the working directory so the systemd StateDirectory holds it)
// so they survive a bot restart. A thread-safe snapshot is written after each
// mutation; reads hit the in-memory map.
public class WarnService(string storePath)
{
    private readonly ConcurrentDictionary<string, WarnRecord> _warns = new();

    public WarnService() : this(Path.Combine(AppContext.BaseDirectory, "warns.json"))
    {
    }

    private string StorePath { get; } = storePath;

    public string AddWarn(ulong guildId, ulong userId, ulong moderatorId, string reason)
    {
        var caseId = $"{guildId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{Guid.NewGuid():N}"[..24];
        var record = new WarnRecord(guildId, userId, moderatorId, reason, DateTime.UtcNow, caseId);
        _warns[caseId] = record;
        Persist();
        return caseId;
    }

    public IReadOnlyList<WarnRecord> GetWarns(ulong guildId, ulong? userId = null)
    {
        IEnumerable<WarnRecord> warns = _warns.Values.Where(w => w.GuildId == guildId);
        if (userId.HasValue)
            warns = warns.Where(w => w.UserId == userId.Value);

        return warns.OrderByDescending(w => w.Timestamp).ToList();
    }

    public bool RemoveWarn(string caseId)
    {
        if (!_warns.TryRemove(caseId, out _))
            return false;
        Persist();
        return true;
    }

    public int WarnCount(ulong guildId, ulong userId) =>
        _warns.Values.Count(w => w.GuildId == guildId && w.UserId == userId);

    private void Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_warns.Values.ToList()));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ WarnService: could not persist warnings: {ex.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return;
            var records = JsonSerializer.Deserialize<List<WarnRecord>>(File.ReadAllText(StorePath));
            if (records is null)
                return;
            foreach (var record in records)
                _warns[record.CaseId] = record;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ WarnService: could not load warnings: {ex.Message}");
        }
    }
}
