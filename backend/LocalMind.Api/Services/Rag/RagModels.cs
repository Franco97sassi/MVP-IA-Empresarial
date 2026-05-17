namespace LocalMind.Api.Services.Rag;

public record RagChunkMatch(
    int DocumentId,
    string FileName,
    int ChunkIndex,
    string Content,
    double Score,
    double VectorScore,
    double KeywordScore,
    double RankScore,
    string Preview););

public record RagSearchResult(IReadOnlyList<RagChunkMatch> Matches)
{
    public bool HasContext => Matches.Count > 0;
}
public record RagEvaluationRequest(
    string Query,
    IReadOnlyCollection<int>? ExpectedDocumentIds = null,
    IReadOnlyCollection<int>? DocumentIds = null,
    double? MinSimilarityScore = null,
    int? MaxRetrievedChunks = null);

public record RagEvaluationItem(
    string Query,
    bool HasContext,
    int RetrievedChunks,
    double TopScore,
    double AverageScore,
    bool ExpectedDocumentHit,
    IReadOnlyList<RagChunkMatch> Matches);

public record RagEvaluationSummary(
    int TotalQuestions,
    int QuestionsWithContext,
    int ExpectedHits,
    double ContextCoverage,
    double ExpectedHitRate,
    double AverageTopScore,
    IReadOnlyList<RagEvaluationItem> Items);