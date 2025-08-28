using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThornBot.Handlers;
using ThornBot.Services;
using DotNetEnv;
using Victoria;

namespace ThornBot;

public class ThornBot : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly EventsHandler _eventsHandler;
    private readonly CommandHandler _commandHandler;
    private readonly LavaLinkService _lavaLink;
    private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
    public static DateTime StartTime;

    public ThornBot()
    {
        // Load environment variables
        Env.Load();

        // Setup config path
        var configPath = Path.Combine(AppContext.BaseDirectory, "Resources");
        Directory.CreateDirectory(configPath);

        var config = new ConfigurationBuilder()
            .SetBasePath(configPath)
            .AddJsonFile("config.json", optional: false, reloadOnChange: true)
            .Build();

        // Configure DI
        _services = ConfigureServices(config);
        _config = _services.GetRequiredService<IConfiguration>();
        _client = _services.GetRequiredService<DiscordSocketClient>();
        _eventsHandler = _services.GetRequiredService<EventsHandler>();
        _commandHandler = _services.GetRequiredService<CommandHandler>();
        _lavaNode = _services.GetRequiredService<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>>();
        _lavaLink = _services.GetRequiredService<LavaLinkService>();

        // Initialize logging
        _services.GetRequiredService<LoggingService>();
    }

    public async Task StartAsync()
    {
        var token = Environment.GetEnvironmentVariable("TOKEN") ?? _config["token"];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("❌ Bot token not found in .env or config.json!");
        
        _lavaLink.StartLavalink("Lavalink.jar");
        await LavaLinkService.WaitForLavalinkAsync(_config["lavalink:hostname"] ?? "localhost", _config["lavalink:port"] is not null ? int.Parse(_config["lavalink:port"]!) : 2333);
        
        await _commandHandler.InitializeAsync();

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        _client.Ready += _eventsHandler.OnReadyAsync;
        
        var icecast = _services.GetRequiredService<IcecastService>();
        _ = icecast.StartMonitoringAsync();
        Console.WriteLine("✅ Icecast 2 service started successfully!");

        StartTime = DateTime.Now;
        Console.WriteLine("✅ Bot started successfully!");
        
        var uptimeService = _services.GetRequiredService<UptimeService>();
        _ = uptimeService.StartMonitoringAsync();
        Console.WriteLine("✅ Uptime monitoring service started successfully!");

        await Task.Delay(Timeout.Infinite);
    }

    private static ServiceProvider ConfigureServices(IConfiguration config) =>
        new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton(x => new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.MessageContent |
                                 GatewayIntents.Guilds |
                                 GatewayIntents.GuildMessages |
                                 GatewayIntents.GuildMessageReactions |
                                 GatewayIntents.DirectMessages |
                                 GatewayIntents.GuildVoiceStates |
                                 GatewayIntents.GuildMembers,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 1000
            }))
            .AddLogging()
            .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
            .AddLavaNode<LavaNode<LavaPlayer<LavaTrack>, LavaTrack>, LavaPlayer<LavaTrack>, LavaTrack>(x =>
            {
                x.Hostname = config["lavalink:hostname"] ?? "localhost";
                x.Port = config["lavalink:port"] is not null ? ushort.Parse(config["lavalink:port"]!) : 2333;
                x.Authorization = config["lavalink:authorization"] ?? "youshallnotpass";
                x.SelfDeaf = config["lavalink:selfdeaf"] is not null && bool.Parse(config["lavalink:selfdeaf"]!);
            })
            .AddSingleton<EventsHandler>()
            .AddSingleton<CommandHandler>()
            .AddSingleton<LoggingService>()
            .AddSingleton<AudioService>()
            .AddSingleton<UptimeService>(sp => new UptimeService(
                config["discord:uptimeKumaPushUrl"] ?? throw new InvalidOperationException("Uptime pushUrl not configured.")))
            .AddSingleton<LavaLinkService>()
            .AddSingleton<IcecastService>(sp => new IcecastService(
                sp.GetRequiredService<DiscordSocketClient>(),
                config["icecast:url"] ?? throw new InvalidOperationException("Icecast URL not configured."),
                ulong.Parse(config["icecast:notifyChannelId"] ?? throw new InvalidOperationException("Icecast notifyChannelId not configured.")),
                ulong.Parse(config["icecast:radioChannelId"] ?? throw new InvalidOperationException("Icecast voiceChannelId not configured.")),
                ulong.Parse(config["icecast:radioGuildId"] ?? throw new InvalidOperationException("Icecast guildId not configured.")),
                sp
            ))
            .BuildServiceProvider();

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable asyncClient)
            await asyncClient.DisposeAsync();

        switch (_services)
        {
            case IAsyncDisposable asyncServices:
                await asyncServices.DisposeAsync();
                break;
            case IDisposable disposableServices:
                disposableServices.Dispose();
                break;
        }

        GC.SuppressFinalize(this);
    }
}
