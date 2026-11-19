using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace MusicStructure;

record Command(string? Action, string? Key, string? Mode, string? Fixture,
    int Pid, string? Started, double T0, string? SessionId);
record ClockReply(double T0, double T1, double T2);

sealed class TickString : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        long.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<Command?> Read(WebSocket socket, CancellationToken token)
    {
        var bytes = new byte[2048];
        int received = 0;
        do
        {
            var part = await socket.ReceiveAsync(bytes.AsMemory(received), token);
            if (part.MessageType == WebSocketMessageType.Close) return null;
            if (part.MessageType != WebSocketMessageType.Text) throw new JsonException("Text commands only.");
            received += part.Count;
            if (part.EndOfMessage) return JsonSerializer.Deserialize<Command>(bytes.AsSpan(0, received), Json);
        } while (received < bytes.Length);
        throw new JsonException("Oversized command.");
    }

    public static async Task Run(WebSocket socket, Session session, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        var clock = Channel.CreateBounded<(double T0, double T1)>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true
        });
        var input = Receive();
        var output = Send();
        await Task.WhenAny(input, output);
        await lifetime.CancelAsync();
        await Task.WhenAll(input, output);

        async Task Receive()
        {
            while (await Read(socket, token) is { } message)
            {
                try
                {
                    switch (message.Action)
                    {
                        case "sources": session.Refresh(); break;
                        case "start":
                            long.TryParse(message.Started, CultureInfo.InvariantCulture, out long started);
                            session.Start(message.Mode ?? "", message.Fixture, message.Pid, started);
                            break;
                        case "stop": await session.Stop(); break;
                        case "new": session.NewListen(); break;
                        case "refine": await session.Refine(message.SessionId, token); break;
                        case "clock": clock.Writer.TryWrite((message.T0, Clock.Milliseconds)); break;
                        default: throw new InvalidOperationException("Unknown action.");
                    }
                }
                catch (Exception ex)
                {
                    session.Notice(ex.Message);
                }
            }
        }

        async Task Send()
        {
            var cursor = new Cursor();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            do
            {
                if (clock.Reader.TryRead(out var ping))
                    await SendJson(new ClockReply(ping.T0, ping.T1, Clock.Milliseconds), socket, token);
                if (session.Read(cursor) is { } batch) await SendJson(batch, socket, token);
            } while (await timer.WaitForNextTickAsync(token));
        }
    }

    static async Task SendJson<T>(T value, WebSocket socket, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, Json), WebSocketMessageType.Text,
            true, deadline.Token);
    }
}
