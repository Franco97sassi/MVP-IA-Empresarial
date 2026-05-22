using LocalMind.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminController(AppDbContext context) => _context = context;

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var users = await _context.Users.CountAsync(cancellationToken);
        var documents = await _context.Documents.CountAsync(cancellationToken);
        var storage = await _context.Documents.SumAsync(d => (long?)d.SizeBytes, cancellationToken) ?? 0;
        var requests24h = await _context.AuditLogs.CountAsync(a => a.CreatedAt >= DateTime.UtcNow.AddHours(-24), cancellationToken);

        return Ok(new { users, documents, storageBytes = storage, requests24h });
    }
}
