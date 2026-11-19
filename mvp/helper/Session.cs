using System.Threading.Channels;

namespace MusicStructure;

sealed class Cursor
{
    public string? Session;
    public long Revision = -1, Marks = -1, Sources = -1, State = -1, Frame, Sequence;
}

sealed class Session
{
    readonly object gate = new();
    Listen? listen;
    CancellationTokenSource? stop;
    Task? running;
    string state = "ready", status = "Choose Spotify or open a fixture.";
    long stateRevision, sourceRevision;
    Source[] sources = [];

    public void Refresh()
    {
        Source[] result = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && Environment.Is64BitProcess
            ? Wasapi.Sources() : [];
        lock (gate)
        {
            sources = result;
            sourceRevision++;
        }
    }

    public void Notice(string message)
    {
        lock (gate) SetState(state, message);
    }

    public void Start(string mode, string? fixture, int pid, long started)
    {
        lock (gate)
        {
            if (running is not null) throw new InvalidOperationException("Stop the current capture first.");
            Source? target = null;
            string label;
            if (mode == "spotify")
            {
                target = sources.FirstOrDefault(s => s.Pid == pid && s.Started == started)
                    ?? throw new InvalidOperationException("Refresh and select a Spotify desktop process.");
                label = target.Label;
            }
            else
            {
                if (mode is not ("fixture" or "replay") || !Fixtures.Names.Contains(fixture))
                    throw new InvalidOperationException("Choose a known fixture.");
                label = fixture!;
            }

            stop = new CancellationTokenSource();
            var token = stop.Token;
            double epoch = mode == "replay" ? 0 : Clock.Milliseconds;
            bool resume = mode == "spotify" && listen?.Mode == "spotify";
            if (resume)
            {
                listen!.Source = label;
                listen.Gap(epoch, "Capture restarted");
            }
            else listen = new Listen(epoch, mode, label);
            SetState(mode == "replay" ? "loading" : "waiting",
                mode == "spotify" ? "Waiting for local Spotify audio." : "Preparing generated fixture.");
            running = Task.Run(async () =>
            {
                try
                {
                    if (mode == "replay")
                    {
                        var replay = AnalyzeFixture(fixture!, token);
                        lock (gate) listen = replay;
                    }
                    else await Stream(target, fixture, epoch, token);

                    lock (gate) SetState("stopped", mode == "spotify" ? "Capture stopped." : "Fixture map ready to review.");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    lock (gate) SetState("stopped", "Capture stopped.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    lock (gate)
                    {
                        if (mode == "spotify" && listen!.Elapsed > 0)
                            listen.Gap(Clock.Milliseconds, "Spotify capture interrupted");
                        SetState("unavailable", mode == "spotify"
                            ? $"Spotify capture unavailable (0x{ex.HResult:X8}). {ex.Message}" : ex.Message);
                    }
                }
                finally
                {
                    lock (gate)
                    {
                        stop?.Dispose();
                        stop = null;
                        running = null;
                    }
                }
            });
        }
    }

    async Task Stream(Source? target, string? fixture, double epoch, CancellationToken token)
    {
        var channel = Channel.CreateBounded<Packet>(new BoundedChannelOptions(24)
        {
            SingleReader = true, SingleWriter = true,
            FullMode = target is null ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest
        }, packet =>
        {
            if (packet.IsGap) return;
            lock (gate)
            {
                if (packet.StartMs < listen!.StartedMs) return;
                listen.DroppedPackets++;
                listen.DroppedFrames += packet.Frames;
                listen.LastDropped = (packet.StartMs, packet.StartMs + packet.Frames * 1000.0 / Packet.Rate);
            }
        });
        var producer = Task.Factory.StartNew(() =>
        {
            try
            {
                if (target is not null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                    Wasapi.Capture(target, packet => channel.Writer.TryWrite(packet), token);
                else if (fixture is not null)
                {
                    foreach (var packet in Fixtures.Packets(fixture))
                    {
                        double due = epoch + packet.StartMs + packet.Frames * 1000.0 / Packet.Rate;
                        int delay = (int)(due - Clock.Milliseconds);
                        if (delay > 0 && token.WaitHandle.WaitOne(delay)) break;
                        token.ThrowIfCancellationRequested();
                        channel.Writer.WriteAsync(packet with { StartMs = epoch + packet.StartMs }, token)
                            .AsTask().GetAwaiter().GetResult();
                    }
                }
                channel.Writer.TryComplete();
            }
            catch (Exception ex) { channel.Writer.TryComplete(ex); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            await foreach (var packet in channel.Reader.ReadAllAsync())
            {
                lock (gate)
                {
                    var current = listen!;
                    if (packet.StartMs < current.StartedMs) continue;
                    current.Add(packet, includePaintClock: true);
                    bool audible = current.InputDb is >= -58 && !packet.IsGap;
                    string connection = target is null ? "Generated fixture" : "Spotify process loopback";
                    if (state != "stopping")
                        SetState(audible ? "capturing" : "waiting", audible ? connection :
                            packet.Interruption ?? (target is null ? "Fixture silence" : "Waiting for local Spotify audio."));
                }
            }
        }
        finally
        {
            lock (gate) stop?.Cancel();
            await producer;
            // A final overflow still needs a gap if no later packet arrived to expose the sequence break.
            lock (gate) listen!.Finish();
        }
    }

    void SetState(string next, string message)
    {
        if (state == next && status == message) return;
        state = next;
        status = message;
        stateRevision++;
    }

    public Task Stop()
    {
        lock (gate)
        {
            if (running is null) return Task.CompletedTask;
            SetState("stopping", "Stopping capture…");
            stop!.Cancel();
            return running;
        }
    }

    public void NewListen()
    {
        lock (gate)
        {
            if (running is null || listen?.Mode != "spotify" || state is not ("capturing" or "waiting"))
                throw new InvalidOperationException("Start Spotify capture before beginning a new listen.");
            listen = new Listen(Clock.Milliseconds, "spotify", listen.Source);
            SetState("waiting", "New listen. Waiting for local Spotify audio.");
        }
    }

    public Batch? Read(Cursor cursor)
    {
        lock (gate)
        {
            string id = listen?.Id ?? "";
            bool snapshot = cursor.Session != id;
            long revision = listen?.Revision ?? 0;
            if (!snapshot && cursor.Revision == revision && cursor.State == stateRevision && cursor.Sources == sourceRevision)
                return null;

            Feature[] frames = listen?.Frames.Where(f => snapshot || f.Index > cursor.Frame).ToArray() ?? [];
            long markRevision = listen?.MarkRevision ?? 0;
            Mark[]? marks = snapshot || cursor.Marks != markRevision ? listen?.Marks.ToArray() ?? [] : null;
            var batch = new Batch(1, Listen.AnalysisVersion, id, ++cursor.Sequence, state, status,
                listen?.Mode, listen?.Source, listen?.Elapsed ?? 0, listen?.HistoryStart ?? 0, listen?.InputDb,
                listen?.Counters ?? new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), frames, marks, snapshot,
                cursor.Sources != sourceRevision ? sources : null,
                OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && Environment.Is64BitProcess);
            if (snapshot) cursor.Frame = 0;
            if (frames.Length > 0) cursor.Frame = frames[^1].Index;
            cursor.Session = id;
            cursor.Revision = revision;
            cursor.State = stateRevision;
            cursor.Marks = markRevision;
            cursor.Sources = sourceRevision;
            return batch;
        }
    }

    public static Listen AnalyzeFixture(string name, CancellationToken stop = default)
    {
        var result = new Listen(0, "replay", name);
        foreach (var packet in Fixtures.Packets(name))
        {
            stop.ThrowIfCancellationRequested();
            result.Add(packet, includePaintClock: false);
        }
        result.Finish();
        return result;
    }
}
