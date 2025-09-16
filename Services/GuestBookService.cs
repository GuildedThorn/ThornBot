using ThornBot.Handlers;

using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using IChannel = RabbitMQ.Client.IChannel;
using Serilog;

namespace ThornBot.Services;

public class GuestBookService {
    private readonly IChannel _channel;
    private readonly IConfiguration _configuration ;
    private readonly DiscordSocketClient _client;

    public GuestBookService(IConfiguration configuration, DiscordSocketClient client)
    {
        _configuration = configuration;
        _client = client;
        
        var factory = new ConnectionFactory()
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMQ__HostName")  ?? "localhost",
            Port = ushort.Parse(Environment.GetEnvironmentVariable("RabbitMQ__Port") ?? "5672"),
            UserName = Environment.GetEnvironmentVariable("RabbitMQ__UserName") ?? "guest",
            Password = Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "guest",
            VirtualHost = Environment.GetEnvironmentVariable("RabbitMQ__VirtualHost") ?? "/"
        };

        var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _channel = connection.CreateChannelAsync().GetAwaiter().GetResult();

        // Ensure the queue exists
        _channel.QueueDeclareAsync(
            queue: "guestbook_messages",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null).GetAwaiter().GetResult();
    }

    public async Task StartAsync()
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                var entry = JsonSerializer.Deserialize<GuestbookEntry>(message);
                if (entry == null) return;

                var guildId = ulong.Parse(_configuration["Discord:GuestbookGuildId"] ?? throw new InvalidOperationException());
                var channelId = ulong.Parse(_configuration["Discord:GuestbookChannelId"] ?? throw new InvalidOperationException());

                var guild = _client.GetGuild(guildId);
                if (guild == null)
                {
                    Console.WriteLine("[!] Guild not found. Make sure the bot is in the guild.");
                    return;
                }

                var channel = guild.GetChannel(channelId);
                if (channel is IMessageChannel messageChannel) {
                    await messageChannel.SendMessageAsync(embed: await EmbedHandler.CreateBasicEmbedWithFields(
                        "ThornBot",
                        "A new guestbook entry has been added.",
                        [
                            new EmbedFieldBuilder().WithName("Name").WithValue(entry.Name).WithIsInline(true),
                            new EmbedFieldBuilder().WithName("Message").WithValue(entry.Message).WithIsInline(false),
                            new EmbedFieldBuilder().WithName("Date").WithValue(entry.Date.ToString("yyyy-MM-dd HH:mm:ss")).WithIsInline(true)
                        ]));
                }
                else
                {
                    Console.WriteLine("[!] Discord channel not found.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Error processing message: " + ex);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: "guestbook_messages",
            autoAck: true,
            consumer: consumer
        );

        Log.Information("✅ GuestBookService RabbitMQ consumer started.");
    }
}

public record GuestbookEntry(string Name, string Message, DateTime Date);
