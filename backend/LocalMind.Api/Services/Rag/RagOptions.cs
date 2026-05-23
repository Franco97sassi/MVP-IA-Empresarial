namespace LocalMind.Api.Services.Rag;

public class RagOptions
{
    public string StorageRoot { get; set; } = "../../rag";

    public int ChunkSize { get; set; } = 900;

    public int ChunkOverlap { get; set; } = 150;

    public int MaxRetrievedChunks { get; set; } = 4;

    public double MinSimilarityScore { get; set; } = 0.2;

    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxDocumentsPerUser { get; set; } = 2;
    public long MaxStorageBytesPerUser { get; set; } = 200 * 1024 * 1024;
    public string EmbeddingModel { get; set; } = "all-minilm";
    public string VectorStoreProvider { get; set; } = "Local";

    public string QdrantUrl { get; set; } = "http://localhost:6333";

    public string QdrantCollection { get; set; } = "localmind_chunks";
}
