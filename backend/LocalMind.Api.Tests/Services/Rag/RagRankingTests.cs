using LocalMind.Api.Services.Rag;

namespace LocalMind.Api.Tests.Services.Rag;

public class RagRankingTests
{
    [Fact]
    public void CalculateKeywordScore_ReturnsHigherScoreForMatchingTerms()
    {
        var matchingScore = RagService.CalculateKeywordScore(
            "arquitectura rag qdrant",
            "La arquitectura RAG usa Qdrant para búsqueda vectorial.");

        var unrelatedScore = RagService.CalculateKeywordScore(
            "arquitectura rag qdrant",
            "Contenido sobre autenticación y métricas.");

        Assert.True(matchingScore > unrelatedScore);
        Assert.True(matchingScore > 0.5);
    }

    [Fact]
    public void CalculateRankScore_BlendsVectorAndKeywordSignals()
    {
        var scoreWithKeywords = RagService.CalculateRankScore(0.7, 0.8);
        var scoreWithoutKeywords = RagService.CalculateRankScore(0.7, 0);

        Assert.True(scoreWithKeywords > scoreWithoutKeywords);
        Assert.Equal(0.725, scoreWithKeywords, precision: 3);
    }
}
