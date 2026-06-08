namespace LocalMind.Api.DTOs.Documents;

public class DocumentResponse
{
    public int Id { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Status { get; set; } = string.Empty;

    public int ChunkCount { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class DocumentChunkResponse
{
    public int Id { get; set; }

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;
}

public class RagSourceResponse
{
    public int DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public int ChunkIndex { get; set; }

    public double Score { get; set; }
    public double VectorScore { get; set; }

    public double KeywordScore { get; set; }

    public double RankScore { get; set; }

    public string Preview { get; set; } = string.Empty;

    public string ChunkReference { get; set; } = string.Empty;

    public string RetrievalProvider { get; set; } = "Local";
}
