using LocalMind.Api.DTOs.Documents;

namespace LocalMind.Api.Services.Rag;

public record RagSearchOptions
{
    public IReadOnlyCollection<int> DocumentIds { get; init; } = Array.Empty<int>();

    public double? MinSimilarityScore { get; init; }

    public int? MaxRetrievedChunks { get; init; }
}

public interface IRagService
{
    Task<DocumentResponse> UploadDocumentAsync(
        int userId,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentResponse>> GetDocumentsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentChunkResponse>> GetDocumentChunksAsync(
        int userId,
        int documentId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteDocumentAsync(
        int userId,
        int documentId,
        CancellationToken cancellationToken = default);
    Task<DocumentResponse?> ReindexDocumentAsync(
       int userId,
       int documentId,
       CancellationToken cancellationToken = default);

    Task<RagSearchResult> SearchAsync(
        int userId,
        string query,
        RagSearchOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RagEvaluationSummary> EvaluateAsync(
        int userId,
        IReadOnlyCollection<RagEvaluationRequest> requests,
        CancellationToken cancellationToken = default);
}