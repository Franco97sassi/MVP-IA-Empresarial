using System.Collections.Concurrent;
using System.Security.Claims;

namespace LocalMind.Api.Middleware;

public class UserRateLimitMiddleware
{
    private static readonly ConcurrentDictionary<string, List<DateTime>> Requests = new();
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;

    public UserRateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task Invoke(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anon";

        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-1);
        var limit = _options.RequestsPerMinute;

        var bucket = Requests.GetOrAdd(userId, _ => new List<DateTime>());
        int remaining;

        lock (bucket)
        {
            bucket.RemoveAll(x => x < windowStart);

            if (bucket.Count >= limit)
            {
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = "60";
                context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = "0";
                await context.Response.WriteAsJsonAsync(new { message = "Rate limit excedido." });
                return;
            }

            bucket.Add(now);
            remaining = Math.Max(0, limit - bucket.Count);
        }

        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();

        await _next(context);
    }
}