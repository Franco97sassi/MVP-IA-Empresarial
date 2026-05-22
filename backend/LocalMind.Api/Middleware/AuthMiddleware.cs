using LocalMind.Api.Models;
using System.Security.Claims;

db.AuditLogs.Add(new AuditLog
{
    UserId = int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null,
    Path = context.Request.Path,
    Method = context.Request.Method,
    StatusCode = context.Response.StatusCode,
    DurationMs = sw.ElapsedMilliseconds,
    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
    TraceId = context.TraceIdentifier
});