using System.Runtime.InteropServices;

namespace ThornBot;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();

        // ctx.Cancel = true suppresses the default terminate-immediately behavior
        // for the signal, giving us a chance to shut down gracefully instead.
        // SIGINT is cancelable on Windows and Unix; SIGTERM only on Unix
        // (the only platform ThornBot is deployed on), per:
        // https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.posixsignalregistration.create
        using var sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleShutdownSignal);
        using var sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleShutdownSignal);

        await using var bot = new ThornBot();
        await bot.StartAsync(cts.Token);
        return;

        void HandleShutdownSignal(PosixSignalContext ctx)
        {
            ctx.Cancel = true;
            Console.WriteLine($"ℹ️ Received {ctx.Signal}, shutting down gracefully...");
            cts.Cancel();
        }
    }
}