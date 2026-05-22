using System.Diagnostics;
using System.Security.Claims;
using LocalMind.Api.Data;
using LocalMind.Api.Models;

namespace LocalMind.Api.Middleware;

public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, AppDbContext db)
    {
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        db.AuditLogs.Add(new AuditLog
        {
            UserId = int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null,
            Path = context.Request.Path,
            Method = context.Request.Method,
            StatusCode = context.Response.StatusCode,
            DurationMs = sw.ElapsedMilliseconds
        });
        await db.SaveChangesAsync();
    }
}
