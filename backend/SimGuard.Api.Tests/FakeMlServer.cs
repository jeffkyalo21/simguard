using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SimGuard.Api.Tests;

// A real HTTP server on a free local port that stands in for the ML
// service. Each test sets Handler; every request is recorded.
public sealed class FakeMlServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public sealed record Received(string Method, string Path, string? Secret, string Body);

    public ConcurrentQueue<Received> Requests { get; } = new();

    public Func<HttpContext, string, Task> Handler { get; set; } = (ctx, _) =>
    {
        ctx.Response.StatusCode = 404;
        return Task.CompletedTask;
    };

    public string BaseUrl { get; }

    private FakeMlServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<FakeMlServer> StartAsync()
    {
        var port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        var server = new FakeMlServer(app, $"http://127.0.0.1:{port}");

        app.Run(async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var secret = ctx.Request.Headers.TryGetValue("X-SimGuard-ML-Secret", out var v) ? v.ToString() : null;
            server.Requests.Enqueue(new Received(ctx.Request.Method, ctx.Request.Path, secret, body));
            await server.Handler(ctx, body);
        });

        await app.StartAsync();
        return server;
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task Json(HttpContext ctx, int status, string json)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = status >= 400 ? "application/problem+json" : "application/json";
        await ctx.Response.WriteAsync(json);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
