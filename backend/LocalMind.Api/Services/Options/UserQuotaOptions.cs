namespace LocalMind.Api.Options;

public class UserQuotaOptions
{
    public int MaxDocumentsPerUser { get; set; } = 5;
    public long MaxStorageBytesPerUser { get; set; } = 10 * 1024 * 1024; // 10MB
}