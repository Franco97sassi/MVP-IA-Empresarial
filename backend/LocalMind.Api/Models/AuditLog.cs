namespace LocalMind.Api.Models;

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
