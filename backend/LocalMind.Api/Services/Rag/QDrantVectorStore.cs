using System.Net;
using System.Text;
using System.Text.Json;
using LocalMind.Api.Models;
using Microsoft.Extensions.Options;

namespace LocalMind.Api.Services.Rag;

public class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;
    private readonly LocalVectorStore _fallback;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(
        HttpClient httpClient,
        IOptions<RagOptions> options,
        LocalVectorStore fallback,
        ILogger<QdrantVectorStore> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _fallback = fallback;
        _logger = logger;
    }

    public string ProviderName => "Qdrant";

    public async Task UpsertAsync(IReadOnlyCollection<VectorStoreUpsertItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return;
        }

        var firstVector = items.First().Embedding;
        await EnsureCollectionAsync(firstVector.Count, cancellationToken);

        var payload = new
        {
            points = items.Select(item => new
            {
                id = item.ChunkId,
                vector = item.Embedding,
                payload = new Dictionary<string, object?>
                {
                    ["userId"] = item.UserId,
                    ["documentId"] = item.DocumentId,
                    ["chunkId"] = item.ChunkId,
                    ["fileName"] = item.FileName,
                    ["chunkIndex"] = item.ChunkIndex,
                    ["contentPreview"] = item.Content[..Math.Min(item.Content.Length, 500)]
                }
            })
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/collections/{_options.QdrantCollection}/points?wait=true")
        {
            Content = JsonContent(payload)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Qdrant upsert failed with {Status}: {Detail}", response.StatusCode, detail);
        }
    }

    public async Task<IReadOnlyList<VectorStoreSearchResult>> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        IReadOnlyCollection<DocumentChunk> fallbackChunks,
        VectorStoreSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        var filterMust = new List<object>
        {
            new
            {
                key = "userId",
                match = new { value = options.UserId }
            }
        };

        if (options.DocumentIds.Count > 0)
        {
            filterMust.Add(new
            {
                key = "documentId",
                match = new { any = options.DocumentIds }
            });
        }

        var payload = new
        {
            vector = queryEmbedding,
            limit = Math.Max(options.MaxRetrievedChunks * 8, options.MaxRetrievedChunks),
            with_payload = false,
            score_threshold = options.MinSimilarityScore,
            filter = new { must = filterMust }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{_options.QdrantCollection}/points/search")
        {
            Content = JsonContent(payload)
        };

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Qdrant search failed with {Status}: {Detail}. Falling back to local search.", response.StatusCode, detail);
                return await _fallback.SearchAsync(queryEmbedding, fallbackChunks, options, cancellationToken);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(responseJson);
            if (!document.RootElement.TryGetProperty("result", out var resultElement))
            {
                return Array.Empty<VectorStoreSearchResult>();
            }

            return resultElement
                .EnumerateArray()
                .Select(item => new VectorStoreSearchResult(
                    item.GetProperty("id").GetInt32(),
                    item.GetProperty("score").GetDouble()))
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Qdrant is unavailable. Falling back to local vector search.");
            return await _fallback.SearchAsync(queryEmbedding, fallbackChunks, options, cancellationToken);
        }
    }

    public async Task DeleteDocumentAsync(int userId, int documentId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            points = new
            {
                filter = new
                {
                    must = new object[]
                    {
                        new { key = "userId", match = new { value = userId } },
                        new { key = "documentId", match = new { value = documentId } }
                    }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/collections/{_options.QdrantCollection}/points/delete?wait=true")
        {
            Content = JsonContent(payload)
        };

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Qdrant delete failed with {Status}: {Detail}", response.StatusCode, detail);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Qdrant delete skipped because the service is unavailable.");
        }
    }

    private async Task EnsureCollectionAsync(int vectorSize, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/collections/{_options.QdrantCollection}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            return;
        }

        var payload = new
        {
            vectors = new
            {
                size = vectorSize,
                distance = "Cosine"
            }
        };

        var createResponse = await _httpClient.PutAsync(
            $"/collections/{_options.QdrantCollection}",
            JsonContent(payload),
            cancellationToken);

        if (!createResponse.IsSuccessStatusCode)
        {
            var detail = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Qdrant collection creation failed with {Status}: {Detail}", createResponse.StatusCode, detail);
        }
    }

    private static StringContent JsonContent(object payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }
}
