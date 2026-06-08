using LocalMind.Api.Models;

namespace LocalMind.Api.Services.Rag;

public record VectorStoreUpsertItem(
    int UserId,
    int DocumentId,
    int ChunkId,
    string FileName,
    int ChunkIndex,
    string Content,
    IReadOnlyList<float> Embedding);

public record VectorStoreSearchOptions(
    int UserId,
    IReadOnlyCollection<int> DocumentIds,
    double MinSimilarityScore,
    int MaxRetrievedChunks);

public record VectorStoreSearchResult(
    int ChunkId,
    double VectorScore);

public interface IVectorStore
{
    string ProviderName { get; }
    Task UpsertAsync(IReadOnlyCollection<VectorStoreUpsertItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VectorStoreSearchResult>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        IReadOnlyCollection<DocumentChunk> fallbackChunks,
        VectorStoreSearchOptions options,
        CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(int userId, int documentId, CancellationToken cancellationToken = default);
}
