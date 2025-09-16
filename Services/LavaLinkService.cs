using System.Diagnostics;
using System.Net.Sockets;
using Serilog;

namespace ThornBot.Services;

public class LavaLinkService : IAsyncDisposable {
    
    private Process? _lavalinkProcess;

    public bool IsRunning =>
        _lavalinkProcess is { HasExited: false };

    public bool LavalinkAlreadyRunning()
    {
        // looks for any Lavalink.jar java process
        return Process.GetProcessesByName("java")
            .Any(p =>
            {
                try
                {
                    return p.MainWindowTitle.Contains("Lavalink", StringComparison.OrdinalIgnoreCase) ||
                           p.StartInfo.Arguments.Contains("Lavalink.jar", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false; // access denied (system processes etc.)
                }
            });
    }

    public void StartLavalink(string jarPath, string? workingDir = null, string javaPath = "java")
    {
        if (LavalinkAlreadyRunning())
        {
            Log.Debug("ℹ️ Lavalink already running, skipping spawn.");
            return;
        }

        if (!File.Exists(jarPath))
            throw new FileNotFoundException("Lavalink.jar not found", jarPath);

        var psi = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-jar \"{jarPath}\"",
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(jarPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _lavalinkProcess = Process.Start(psi);

        _lavalinkProcess.OutputDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                // Optional: write to a file instead of console
                File.AppendAllText("lavalink.log", $"[OUT] {e.Data}{Environment.NewLine}");
            }
        };
        _lavalinkProcess.BeginOutputReadLine();


        _lavalinkProcess.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                File.AppendAllText("lavalink.log", $"[ERR] {e.Data}{Environment.NewLine}");
            }
        };
        _lavalinkProcess.BeginErrorReadLine();

        Log.Information("✅ Lavalink started by bot.");
    }
    
    public static async Task WaitForLavalinkAsync(string host, int port, int timeoutSeconds = 30)
    {
        using var client = new TcpClient();
        var start = DateTime.Now;

        while ((DateTime.Now - start).TotalSeconds < timeoutSeconds)
        {
            try
            {
                await client.ConnectAsync(host, port);
                if (client.Connected)
                {
                    Log.Information("✅ Lavalink is up!");
                    return;
                }
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("❌ Lavalink did not start in time.");
    }


    public async ValueTask DisposeAsync()
    {
        if (_lavalinkProcess is { HasExited: false })
        {
            _lavalinkProcess.Kill();
            await _lavalinkProcess.WaitForExitAsync();
        }
        _lavalinkProcess?.Dispose();
    }
}
