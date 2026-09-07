using Microsoft.Extensions.Configuration;

namespace ThornBot.Services;

// Hard-fails startup when known-insecure defaults are left in place. The bot
// ships with illustrative placeholder credentials (youshallnotpass,
// guest/guest); accidentally running one of those against a live network is a
// credential-disclosure risk, especially once moderation shadows perms Discord
// enforces. This is intentionally fatal, not a warning — fail closed rather
// than silently operate on default credentials.
//
// Local developers can opt out of the password checks with
// THORNBOT_ALLOW_DEFAULT_SECRETS=1 (dev/up.sh no longer needs to, because it
// generates real random secrets, but the escape hatch exists for throwaway
// setup). Production (the Nix systemd unit) never sets it.
public static class SecurityValidator
{
    private const string DefaultLavalinkPassword = "youshallnotpass";

    public static void Validate(IConfiguration config)
    {
        if (Environment.GetEnvironmentVariable("THORNBOT_ALLOW_DEFAULT_SECRETS") == "1")
            return;

        ValidateLavalink(config);
        ValidateRabbitMq(config);
    }

    private static void ValidateLavalink(IConfiguration config)
    {
        var password = config["lavalink:authorization"];
        if (string.Equals(password, DefaultLavalinkPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to start: Lavalink password is still the default 'youshallnotpass'. " +
                "Set Lavalink__Authorization (or Resources/config.json lavalink.authorization) to a strong secret.");
        }

        // Lavalink is spawned as a local child process — it should never be
        // reachable off the box. Requiring localhost also prevents a
        // misconfiguration that would expose the audio framework to the LAN.
        var hostname = config["lavalink:hostname"] ?? "localhost";
        if (hostname != "localhost" && hostname != "127.0.0.1")
        {
            throw new InvalidOperationException(
                $"Refusing to start: Lavalink hostname '{hostname}' is not loopback. " +
                "ThornBot spawns its own Lavalink on localhost; do not point it at a remote instance.");
        }
    }

    private static void ValidateRabbitMq(IConfiguration config)
    {
        var user = Environment.GetEnvironmentVariable("RabbitMQ__UserName")
                   ?? config["rabbitmq:userName"]
                   ?? "guest";
        var password = Environment.GetEnvironmentVariable("RabbitMQ__Password")
                       ?? config["rabbitmq:password"]
                       ?? "guest";

        if (user == "guest" && password == "guest")
        {
            throw new InvalidOperationException(
                "Refusing to start: RabbitMQ is using the default guest/guest credentials. " +
                "Create a dedicated user and supply it via RabbitMQ__UserName / RabbitMQ__Password.");
        }
    }
}
