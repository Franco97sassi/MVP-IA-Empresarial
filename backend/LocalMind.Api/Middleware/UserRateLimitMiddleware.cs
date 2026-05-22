using System.Collections.Concurrent;
using System.Security.Claims;

namespace LocalMind.Api.Middleware;

public class UserRateLimitMiddleware
{
    private static readonly ConcurrentDictionary<string, List<DateTime>> Requests = new();
    private readonly RequestDelegate _next;

    public UserRateLimitMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anon";
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-1);

        var bucket = Requests.GetOrAdd(userId, _ => new List<DateTime>());
        lock (bucket)
        {
            bucket.RemoveAll(x => x < windowStart);
            if (bucket.Count >= 60)
            {
                context.Response.StatusCode = 429;
                return;
            }
            bucket.Add(now);
        }

        await _next(context);
    }
}
