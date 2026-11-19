using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MusicStructure;

if (args is ["--verify"]) return Verification.Run();
if (args.Any(arg => arg is not ("--no-open" or "--demo")))
{
    Console.Error.WriteLine("Usage: MusicStructure [--no-open] [--demo] | --verify");
    return 2;
}

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
builder.Logging.ClearProviders();
builder.Configuration.Sources.Clear();
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Listen(IPAddress.Loopback, 0);
});
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(5), KeepAliveTimeout = TimeSpan.FromSeconds(10) });
var session = new Session();
string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
string origin = "";
int attached = 0;

app.Use(async (context, next) =>
{
    if (origin.Length == 0 || context.Request.Host.Value != origin["http://".Length..])
    {
        context.Response.StatusCode = 400;
        return;
    }
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; connect-src 'self' " + origin.Replace("http:", "ws:") +
        "; frame-ancestors 'none'; object-src 'none'; base-uri 'none'";
    await next(context);
});
app.MapGet("/session", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest ||
        !string.Equals(context.Request.Headers.Origin, origin, StringComparison.Ordinal))
    {
        context.Response.StatusCode = 403;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping);
    using var auth = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
    auth.CancelAfter(TimeSpan.FromSeconds(3));
    bool ownsSession = false;
    try
    {
        var credentials = await Wire.Read(socket, auth.Token);
        if (credentials?.Key is not { Length: 64 } presented ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key), Encoding.ASCII.GetBytes(presented)))
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Open the URL printed by the helper.", auth.Token);
            return;
        }

        ownsSession = Interlocked.CompareExchange(ref attached, 1, 0) == 0;
        if (!ownsSession)
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Close the other listening tab.", auth.Token);
            return;
        }
        try { session.Refresh(); }
        catch (Exception ex) { session.Notice($"Could not list Spotify sources. {ex.Message}"); }
        await Wire.Run(socket, session, lifetime);
    }
    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
    {
        // A closed or malformed socket loses its capture lease.
    }
    finally
    {
        if (ownsSession)
        {
            await session.Stop();
            Interlocked.Exchange(ref attached, 0);
        }
    }
});

var assets = Assembly.GetExecutingAssembly();
app.MapGet("/{**path}", (string? path) =>
{
    string name = string.IsNullOrEmpty(path) ? "index.html" : path;
    var contentType = Path.GetExtension(name) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        _ => null
    };
    var stream = contentType is null ? null : assets.GetManifestResourceStream("viewer/" + name)
        ?? assets.GetManifestResourceStream("viewer/" + name.Replace('/', '\\'));
    return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
});

origin = await StartApp();
string url = $"{origin}/#{key}{(args.Contains("--demo") ? "/demo" : "")}";
Console.WriteLine($"Open {url}");
Console.WriteLine("Keep this helper running. Press Ctrl+C to quit.");
if (!args.Contains("--no-open"))
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch (Exception ex) { Console.Error.WriteLine($"Could not open the browser. {ex.Message}"); }
}
await app.WaitForShutdownAsync();
return 0;

async Task<string> StartApp()
{
    await app.StartAsync();
    return app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
}
