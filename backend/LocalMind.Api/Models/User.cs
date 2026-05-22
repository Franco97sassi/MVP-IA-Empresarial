namespace LocalMind.Api.Models;

public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string Role { get; set; } = "User";

    public List<Conversation> Conversations { get; set; } = new();
    public List<Document> Documents { get; set; } = new();
    public List<ChatMetric> ChatMetrics { get; set; } = new();

    public List<RefreshToken> RefreshTokens { get; set; } = new();
}