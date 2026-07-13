using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ThornBot.Handlers;

namespace ThornBot.Services;

public class GuestBookService(IConfiguration configuration, DiscordSocketClient client, ILogger<GuestBookService> logger)
{
    private const string QueueName = "guestbook_messages";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    // Retries the initial connect indefinitely instead of dying once — RabbitMQ
    // being down when the bot boots shouldn't mean guestbook notifications stay
    // dead until someone manually restarts it. Once connected,
    // ConnectionFactory.AutomaticRecoveryEnabled (on by default in
    // RabbitMQ.Client 7.x) handles reconnecting — and re-subscribing this
    // consumer — if the connection drops later.
    public async Task StartAsync()
    {
        while (true)
        {
            try
            {
                await ConnectAndConsumeAsync();
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "GuestBookService couldn't reach RabbitMQ — retrying in {Delay}", RetryDelay);
                await Task.Delay(RetryDelay);
            }
        }
    }

    private async Task ConnectAndConsumeAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMQ__HostName") ?? "localhost",
            Port = ushort.Parse(Environment.GetEnvironmentVariable("RabbitMQ__Port") ?? "5672"),
            UserName = Environment.GetEnvironmentVariable("RabbitMQ__UserName") ?? "guest",
            Password = Environment.GetEnvironmentVariable("RabbitMQ__Password") ?? "guest",
            VirtualHost = Environment.GetEnvironmentVariable("RabbitMQ__VirtualHost") ?? "/",
        };

        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // Must be awaited: publishing to the default exchange before this queue
        // exists is silently dropped by RabbitMQ (no error).
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) => await HandleMessageAsync(ea);

        await channel.BasicConsumeAsync(queue: QueueName, autoAck: true, consumer: consumer);
        logger.LogInformation("GuestBookService RabbitMQ consumer started.");
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea)
    {
        try
        {
            var message = Encoding.UTF8.GetString(ea.Body.ToArray());
            var entry = JsonSerializer.Deserialize<GuestbookEntry>(message);
            if (entry is null) return;

            var guildId = ulong.Parse(configuration["Discord:GuestbookGuildId"] ?? throw new InvalidOperationException());
            var channelId = ulong.Parse(configuration["Discord:GuestbookChannelId"] ?? throw new InvalidOperationException());

            var guild = client.GetGuild(guildId);
            if (guild is null)
            {
                logger.LogWarning("Guestbook guild {GuildId} not found — is the bot in it?", guildId);
                return;
            }

            if (guild.GetChannel(channelId) is not IMessageChannel messageChannel)
            {
                logger.LogWarning("Guestbook channel {ChannelId} not found in guild {GuildId}", channelId, guildId);
                return;
            }

            var embed = await EmbedHandler.CreateBasicEmbed($"📝 New Guestbook Message from {entry.Name}", entry.Message);
            await messageChannel.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error processing guestbook message");
        }
    }
}

public record GuestbookEntry(string Name, string Message, DateTime Date);
