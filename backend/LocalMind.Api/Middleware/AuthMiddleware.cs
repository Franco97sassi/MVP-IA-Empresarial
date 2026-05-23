using System.Security.Claims;

namespace LocalMind.Api.Middleware;

public class AuthMiddleware
{
   
    private readonly RequestDelegate _next;

public AuthMiddleware(RequestDelegate next)
{
    _next = next;
}

public async Task InvokeAsync(HttpContext context)
{
    var isAuthEndpoint = context.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);

    if (!isAuthEndpoint && context.User?.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Token inválido: no se encontró NameIdentifier."
            });
            return;
        }
    }

    await _next(context);
}
}