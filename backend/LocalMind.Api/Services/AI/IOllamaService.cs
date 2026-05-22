namespace LocalMind.Api.Services.Ai;

public interface IOllamaService
{
    //Task<string> SendMessageAsync(string message);
    Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default);

    Task<string> SendMessageAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamMessageAsync(string message, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamMessageAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}