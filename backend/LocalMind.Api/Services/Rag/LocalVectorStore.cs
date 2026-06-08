using LocalMind.Api.Models;

namespace LocalMind.Api.Services.Rag;

public class LocalVectorStore : IVectorStore
{
    private readonly IEmbeddingSerializer _embeddingSerializer;

    public LocalVectorStore(IEmbeddingSerializer embeddingSerializer)
    {
        _embeddingSerializer = embeddingSerializer;
    }

    public string ProviderName => "Local";

    public Task UpsertAsync(IReadOnlyCollection<VectorStoreUpsertItem> items, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(int userId, int documentId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorStoreSearchResult>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        IReadOnlyCollection<DocumentChunk> fallbackChunks,
        VectorStoreSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var results = fallbackChunks
            .Select(chunk => new VectorStoreSearchResult(
                chunk.Id,
                CosineSimilarity(queryEmbedding, _embeddingSerializer.Deserialize(chunk.EmbeddingJson))))
            .Where(result => result.VectorScore >= options.MinSimilarityScore)
            .OrderByDescending(result => result.VectorScore)
            .Take(Math.Max(options.MaxRetrievedChunks * 8, options.MaxRetrievedChunks))
            .ToList();

        return Task.FromResult<IReadOnlyList<VectorStoreSearchResult>>(results);
    }

    private static double CosineSimilarity(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        if (first.Count == 0 || second.Count == 0 || first.Count != second.Count)
        {
            return 0;
        }

        double dot = 0;
        double firstMagnitude = 0;
        double secondMagnitude = 0;

        for (var index = 0; index < first.Count; index++)
        {
            dot += first[index] * second[index];
            firstMagnitude += first[index] * first[index];
            secondMagnitude += second[index] * second[index];
        }

        if (firstMagnitude == 0 || secondMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(firstMagnitude) * Math.Sqrt(secondMagnitude));
    }
}
