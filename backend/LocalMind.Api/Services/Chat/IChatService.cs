namespace LocalMind.Api.Services.Chat;

public interface IChatService
{
    Task<ChatResult> GenerateResponseAsync(
        int userId,
        string message,
        IReadOnlyCollection<int>? documentIds = null,
        CancellationToken cancellationToken = default);
}