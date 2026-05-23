namespace LocalMind.Api.Options;

public class RateLimitOptions
{
    public int RequestsPerMinute { get; set; } = 10;
}