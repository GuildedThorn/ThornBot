using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThornBot.Handlers;
using ThornBot.Services;
using DotNetEnv;

namespace ThornBot;

public class ThornBot : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly DiscordSocketClient _client;
    private readonly IConfiguration _config;
    private readonly CommandHandler _commandHandler;
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
        _commandHandler = _services.GetRequiredService<CommandHandler>();

        // Initialize logging
        _services.GetRequiredService<LoggingService>();

    }

    public async Task StartAsync()
    {
        var token = Environment.GetEnvironmentVariable("TOKEN") ?? _config["token"];
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("❌ Bot token not found in .env or config.json!");

        await _commandHandler.InitializeAsync();

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        StartTime = DateTime.Now;
        Console.WriteLine("✅ Bot started successfully!");

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
            .AddSingleton<CommandHandler>()
            .AddSingleton<LoggingService>()
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
