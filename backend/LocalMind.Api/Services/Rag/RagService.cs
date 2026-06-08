using System.Text.RegularExpressions;
using LocalMind.Api.Data;
using LocalMind.Api.DTOs.Documents;
using LocalMind.Api.Models;
using LocalMind.Api.Services.Ai;
using LocalMind.Api.Services.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LocalMind.Api.Services.Rag;

public class RagService : IRagService
{
    private const string DocumentsFolderName = "documents";
    private const string ChunksFolderName = "chunks";

    private const int PreviewMaxLength = 320;
    private const double VectorWeight = 0.75;
    private const double KeywordWeight = 0.25;

    private static readonly Regex TokenRegex = new(
        @"[\p{L}\p{N}]+",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".txt",
        ".md"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "text/markdown",
        "text/x-markdown",
        "application/octet-stream"
    };

    private readonly AppDbContext _context;
    private readonly IOllamaService _ollamaService;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly ITextChunker _chunker;
    private readonly IEmbeddingSerializer _embeddingSerializer;
    private readonly IEmbeddingCacheService _embeddingCache;
    private readonly IVectorStoreResolver _vectorStoreResolver;
    private readonly IAiTelemetryService _telemetry;
    private readonly RagOptions _options;

    public RagService(
        AppDbContext context,
        IOllamaService ollamaService,
        IDocumentTextExtractor textExtractor,
        ITextChunker chunker,
        IEmbeddingSerializer embeddingSerializer,
        IEmbeddingCacheService embeddingCache,
        IVectorStoreResolver vectorStoreResolver,
        IAiTelemetryService telemetry,
        IOptions<RagOptions> options)
    {
        _context = context;
        _ollamaService = ollamaService;
        _textExtractor = textExtractor;
        _chunker = chunker;
        _embeddingSerializer = embeddingSerializer;
        _embeddingCache = embeddingCache;
        _vectorStoreResolver = vectorStoreResolver;
        _telemetry = telemetry;
        _options = options.Value;
    }

    public async Task<DocumentResponse> UploadDocumentAsync(
        int userId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);

        var currentDocuments = await _context.Documents
            .CountAsync(document => document.UserId == userId, cancellationToken);

        var currentStorage = await _context.Documents
            .Where(document => document.UserId == userId)
            .SumAsync(document => (long?)document.SizeBytes, cancellationToken) ?? 0;

        if (currentDocuments >= _options.MaxDocumentsPerUser)
        {
            throw new InvalidOperationException(
                $"Límite de documentos alcanzado ({_options.MaxDocumentsPerUser}).");
        }

        if (currentStorage + file.Length > _options.MaxStorageBytesPerUser)
        {
            throw new InvalidOperationException(
                "Superaste el límite de almacenamiento para tu cuenta.");
        }

        var safeOriginalFileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(safeOriginalFileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";

        var storageRoot = GetStorageRoot();
        var documentsPath = GetUserDocumentsPath(storageRoot, userId);
        var chunksPath = GetUserChunksPath(storageRoot, userId);

        var storedFilePath = Path.Combine(documentsPath, storedFileName);
        var chunkFilePrefix = Path.GetFileNameWithoutExtension(storedFileName);

        Directory.CreateDirectory(documentsPath);
        Directory.CreateDirectory(chunksPath);

        try
        {
            await SaveUploadedFileAsync(file, storedFilePath, cancellationToken);

            var extractedText = await _textExtractor.ExtractTextAsync(file, cancellationToken);
            var chunks = _chunker.Split(
                extractedText,
                _options.ChunkSize,
                _options.ChunkOverlap);

            if (chunks.Count == 0)
            {
                throw new InvalidOperationException("No se pudo extraer texto útil del documento.");
            }

            var document = new Document
            {
                UserId = userId,
                OriginalFileName = safeOriginalFileName,
                StoredFileName = storedFileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                Status = "Processed"
            };

            for (var index = 0; index < chunks.Count; index++)
            {
                var chunkContent = chunks[index];
                var embedding = await GenerateCachedEmbeddingAsync(chunkContent, cancellationToken);

                var chunkFileName = $"{chunkFilePrefix}-{index}.txt";
                var chunkFilePath = Path.Combine(chunksPath, chunkFileName);

                await File.WriteAllTextAsync(chunkFilePath, chunkContent, cancellationToken);

                document.Chunks.Add(new DocumentChunk
                {
                    ChunkIndex = index,
                    Content = chunkContent,
                    EmbeddingJson = _embeddingSerializer.Serialize(embedding),
                    SourceFileName = safeOriginalFileName
                });
            }

            _context.Documents.Add(document);
            await _context.SaveChangesAsync(cancellationToken);

            await UpsertDocumentVectorsAsync(userId, document, cancellationToken);

            return ToResponse(document);
        }
        catch
        {
            TryDeleteFile(storedFilePath);
            TryDeleteFiles(chunksPath, $"{chunkFilePrefix}-*.txt");
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentResponse>> GetDocumentsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .Where(document => document.UserId == userId)
            .OrderByDescending(document => document.CreatedAt)
            .Select(document => new DocumentResponse
            {
                Id = document.Id,
                OriginalFileName = document.OriginalFileName,
                SizeBytes = document.SizeBytes,
                Status = document.Status,
                ChunkCount = document.Chunks.Count,
                CreatedAt = document.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentChunkResponse>> GetDocumentChunksAsync(
        int userId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var documentExists = await _context.Documents.AnyAsync(
            document => document.Id == documentId && document.UserId == userId,
            cancellationToken);

        if (!documentExists)
        {
            return Array.Empty<DocumentChunkResponse>();
        }

        return await _context.DocumentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new DocumentChunkResponse
            {
                Id = chunk.Id,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteDocumentAsync(
        int userId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(
                item => item.Id == documentId && item.UserId == userId,
                cancellationToken);

        if (document is null)
        {
            return false;
        }

        var storageRoot = GetStorageRoot();
        var storedFilePath = Path.Combine(
            GetUserDocumentsPath(storageRoot, userId),
            document.StoredFileName);

        var chunksPath = GetUserChunksPath(storageRoot, userId);
        var chunkFilePrefix = Path.GetFileNameWithoutExtension(document.StoredFileName);

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        await _vectorStoreResolver
            .Resolve()
            .DeleteDocumentAsync(userId, documentId, cancellationToken);

        TryDeleteFile(storedFilePath);
        TryDeleteFiles(chunksPath, $"{chunkFilePrefix}-*.txt");

        return true;
    }

    public async Task<DocumentResponse?> ReindexDocumentAsync(
        int userId,
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _context.Documents
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(
                item => item.Id == documentId && item.UserId == userId,
                cancellationToken);

        if (document is null)
        {
            return null;
        }

        var storageRoot = GetStorageRoot();
        var storedFilePath = Path.Combine(
            GetUserDocumentsPath(storageRoot, userId),
            document.StoredFileName);

        if (!File.Exists(storedFilePath))
        {
            throw new InvalidOperationException(
                "No se encontró el archivo original del documento para reprocesar.");
        }

        var chunksPath = GetUserChunksPath(storageRoot, userId);
        var chunkFilePrefix = Path.GetFileNameWithoutExtension(document.StoredFileName);

        await using var stream = File.OpenRead(storedFilePath);

        IFormFile formFile = new FormFile(
            stream,
            0,
            stream.Length,
            "file",
            document.OriginalFileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = document.ContentType
        };

        var extractedText = await _textExtractor.ExtractTextAsync(formFile, cancellationToken);
        var chunks = _chunker.Split(
            extractedText,
            _options.ChunkSize,
            _options.ChunkOverlap);

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("No se pudo extraer texto útil del documento.");
        }

        _context.DocumentChunks.RemoveRange(document.Chunks);
        document.Chunks.Clear();

        TryDeleteFiles(chunksPath, $"{chunkFilePrefix}-*.txt");

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunkContent = chunks[index];
            var embedding = await GenerateCachedEmbeddingAsync(chunkContent, cancellationToken);

            var chunkFileName = $"{chunkFilePrefix}-{index}.txt";
            var chunkFilePath = Path.Combine(chunksPath, chunkFileName);

            await File.WriteAllTextAsync(chunkFilePath, chunkContent, cancellationToken);

            document.Chunks.Add(new DocumentChunk
            {
                ChunkIndex = index,
                Content = chunkContent,
                EmbeddingJson = _embeddingSerializer.Serialize(embedding),
                SourceFileName = document.OriginalFileName
            });
        }

        document.Status = "Processed";
        document.Error = null;

        await _context.SaveChangesAsync(cancellationToken);

        await _vectorStoreResolver
            .Resolve()
            .DeleteDocumentAsync(userId, documentId, cancellationToken);

        await UpsertDocumentVectorsAsync(userId, document, cancellationToken);

        return ToResponse(document);
    }

    public async Task<RagSearchResult> SearchAsync(
        int userId,
        string query,
        RagSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var documentIds = options?.DocumentIds?
            .Where(id => id > 0)
            .ToHashSet() ?? new HashSet<int>();

        var minSimilarityScore = options?.MinSimilarityScore ?? _options.MinSimilarityScore;

        var maxRetrievedChunks = Math.Max(
            1,
            options?.MaxRetrievedChunks ?? _options.MaxRetrievedChunks);

        var chunksQuery = _context.DocumentChunks
            .AsNoTracking()
            .Include(chunk => chunk.Document)
            .Where(chunk => chunk.Document.UserId == userId);

        if (documentIds.Count > 0)
        {
            chunksQuery = chunksQuery.Where(chunk => documentIds.Contains(chunk.DocumentId));
        }

        var chunks = await chunksQuery.ToListAsync(cancellationToken);

        if (chunks.Count == 0)
        {
            return new RagSearchResult(Array.Empty<RagChunkMatch>());
        }

        using var measurement = _telemetry.Measure("rag.search", new Dictionary<string, object?>
        {
            ["userId"] = userId,
            ["documents"] = documentIds.Count,
            ["candidateChunks"] = chunks.Count
        });

        var retrievalQuery = _options.EnableQueryRewrite
            ? RewriteQuery(query)
            : query;

        var queryEmbedding = await GenerateCachedEmbeddingAsync(
            retrievalQuery,
            cancellationToken);

        var vectorStore = _vectorStoreResolver.Resolve();

        var vectorResults = await vectorStore.SearchAsync(
            queryEmbedding,
            chunks,
            new VectorStoreSearchOptions(
                userId,
                documentIds,
                minSimilarityScore,
                maxRetrievedChunks),
            cancellationToken);

        var chunksById = chunks.ToDictionary(chunk => chunk.Id);

        var matches = vectorResults
            .Where(result => chunksById.ContainsKey(result.ChunkId))
            .Select(result => BuildMatch(
                query,
                chunksById[result.ChunkId],
                result.VectorScore,
                vectorStore.ProviderName))
            .Where(match => match.Score >= minSimilarityScore)
            .OrderByDescending(match => match.RankScore)
            .ThenByDescending(match => match.VectorScore)
            .ThenByDescending(match => match.KeywordScore)
            .ThenBy(match => match.ChunkIndex)
            .Take(maxRetrievedChunks)
            .ToList();

        return new RagSearchResult(matches);
    }

    public async Task<RagEvaluationSummary> EvaluateAsync(
        int userId,
        IReadOnlyCollection<RagEvaluationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var validRequests = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.Query))
            .ToList();

        var items = new List<RagEvaluationItem>();

        foreach (var request in validRequests)
        {
            var result = await SearchAsync(
                userId,
                request.Query,
                new RagSearchOptions
                {
                    DocumentIds = request.DocumentIds ?? Array.Empty<int>(),
                    MinSimilarityScore = request.MinSimilarityScore,
                    MaxRetrievedChunks = request.MaxRetrievedChunks
                },
                cancellationToken);

            var expectedDocumentIds = request.ExpectedDocumentIds?
                .Where(id => id > 0)
                .ToHashSet() ?? new HashSet<int>();

            var expectedDocumentHit =
                expectedDocumentIds.Count == 0 ||
                result.Matches.Any(match => expectedDocumentIds.Contains(match.DocumentId));

            var topScore = result.Matches.Count > 0
                ? result.Matches.Max(match => match.Score)
                : 0;

            var averageScore = result.Matches.Count > 0
                ? result.Matches.Average(match => match.Score)
                : 0;

            items.Add(new RagEvaluationItem(
                request.Query,
                result.HasContext,
                result.Matches.Count,
                Math.Round(topScore, 4),
                Math.Round(averageScore, 4),
                expectedDocumentHit,
                result.Matches));
        }

        var totalQuestions = items.Count;
        var questionsWithContext = items.Count(item => item.HasContext);
        var expectedHits = items.Count(item => item.ExpectedDocumentHit);

        var averageTopScore = totalQuestions > 0
            ? items.Average(item => item.TopScore)
            : 0;

        var recallAtK = totalQuestions > 0
            ? (double)expectedHits / totalQuestions
            : 0;

        var reciprocalRanks = items.Select(item =>
        {
            if (item.Matches.Count == 0)
            {
                return 0;
            }

            var expected = validRequests
                .First(request => request.Query == item.Query)
                .ExpectedDocumentIds?
                .ToHashSet() ?? new HashSet<int>();

            if (expected.Count == 0)
            {
                return item.HasContext ? 1 : 0;
            }

            for (var index = 0; index < item.Matches.Count; index++)
            {
                if (expected.Contains(item.Matches[index].DocumentId))
                {
                    return 1.0 / (index + 1);
                }
            }

            return 0;
        }).ToList();

        var averageGroundingScore = items.Count > 0
            ? items.Average(item =>
                item.Matches.Count == 0
                    ? 0
                    : item.Matches.Average(match => match.RankScore))
            : 0;

        return new RagEvaluationSummary(
            totalQuestions,
            questionsWithContext,
            expectedHits,
            totalQuestions > 0 ? Math.Round((double)questionsWithContext / totalQuestions, 4) : 0,
            totalQuestions > 0 ? Math.Round((double)expectedHits / totalQuestions, 4) : 0,
            Math.Round(averageTopScore, 4),
            Math.Round(recallAtK, 4),
            Math.Round(reciprocalRanks.Count > 0 ? reciprocalRanks.Average() : 0, 4),
            Math.Round(averageGroundingScore, 4),
            items);
    }

    public static double CalculateKeywordScore(string query, string content)
    {
        var queryTokens = Tokenize(query).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var contentTokens = Tokenize(content).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = queryTokens.Count(contentTokens.Contains);

        return (double)matches / queryTokens.Count;
    }

    public static double CalculateRankScore(double vectorScore, double keywordScore)
    {
        return (vectorScore * VectorWeight) + (keywordScore * KeywordWeight);
    }

    private static string RewriteQuery(string query)
    {
        var expansions = new List<string>();
        var normalized = query.ToLowerInvariant();

        if (normalized.Contains("resumen") || normalized.Contains("resume"))
        {
            expansions.Add("síntesis puntos principales ideas clave");
        }

        if (normalized.Contains("tarea") || normalized.Contains("pendiente"))
        {
            expansions.Add("acciones pendientes próximos pasos responsables");
        }

        if (normalized.Contains("rag") ||
            normalized.Contains("documento") ||
            normalized.Contains("fuente"))
        {
            expansions.Add("retrieval augmented generation búsqueda semántica chunks embeddings fuentes");
        }

        return expansions.Count == 0
            ? query
            : $"{query} {string.Join(' ', expansions)}";
    }

    private RagChunkMatch BuildMatch(
        string query,
        DocumentChunk chunk,
        double vectorScore,
        string retrievalProvider)
    {
        var keywordScore = CalculateKeywordScore(query, chunk.Content);
        var rankScore = CalculateRankScore(vectorScore, keywordScore);

        return new RagChunkMatch(
            chunk.DocumentId,
            chunk.SourceFileName,
            chunk.ChunkIndex,
            chunk.Content,
            rankScore,
            vectorScore,
            keywordScore,
            rankScore,
            BuildPreview(chunk.Content),
            retrievalProvider);
    }

    private async Task<IReadOnlyList<float>> GenerateCachedEmbeddingAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var model = _options.EmbeddingModel;

        using var measurement = _telemetry.Measure("embedding.generate", new Dictionary<string, object?>
        {
            ["model"] = model,
            ["characters"] = text.Length
        });

        return await _embeddingCache.GetOrCreateAsync(
            model,
            text,
            ct => _ollamaService.GenerateEmbeddingAsync(text, ct),
            cancellationToken);
    }

    private async Task UpsertDocumentVectorsAsync(
        int userId,
        Document document,
        CancellationToken cancellationToken)
    {
        var items = document.Chunks
            .Select(chunk => new VectorStoreUpsertItem(
                userId,
                document.Id,
                chunk.Id,
                chunk.SourceFileName,
                chunk.ChunkIndex,
                chunk.Content,
                _embeddingSerializer.Deserialize(chunk.EmbeddingJson)))
            .ToList();

        await _vectorStoreResolver
            .Resolve()
            .UpsertAsync(items, cancellationToken);
    }

    private void ValidateFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            throw new InvalidOperationException("El archivo está vacío.");
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"El archivo supera el límite de {_options.MaxFileSizeBytes / 1024 / 1024} MB.");
        }

        var safeFileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(safeFileName);

        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Solo se permiten archivos PDF, TXT o MD.");
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) &&
            !AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException("El tipo de contenido del archivo no es válido.");
        }
    }

    private string GetStorageRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.StorageRoot));
    }

    private static string GetUserDocumentsPath(string storageRoot, int userId)
    {
        return Path.Combine(storageRoot, DocumentsFolderName, userId.ToString());
    }

    private static string GetUserChunksPath(string storageRoot, int userId)
    {
        return Path.Combine(storageRoot, ChunksFolderName, userId.ToString());
    }

    private static async Task SaveUploadedFileAsync(
        IFormFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(destinationPath);
        await file.CopyToAsync(stream, cancellationToken);
    }

    private static string SanitizeFileName(string fileName)
    {
        var sanitized = Path.GetFileName(fileName).Trim();

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidCharacter, '_');
        }

        return string.IsNullOrWhiteSpace(sanitized)
            ? $"document-{Guid.NewGuid():N}.txt"
            : sanitized;
    }

    private static DocumentResponse ToResponse(Document document)
    {
        return new DocumentResponse
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            SizeBytes = document.SizeBytes,
            Status = document.Status,
            ChunkCount = document.Chunks.Count,
            CreatedAt = document.CreatedAt
        };
    }

    private static string BuildPreview(string content)
    {
        var normalized = string.Join(
            ' ',
            content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= PreviewMaxLength)
        {
            return normalized;
        }

        return normalized[..PreviewMaxLength].TrimEnd() + "…";
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return TokenRegex
            .Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(token => token.Length >= 3);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Limpieza best effort.
        }
    }

    private static void TryDeleteFiles(string directory, string searchPattern)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, searchPattern))
            {
                TryDeleteFile(file);
            }
        }
        catch
        {
            // Limpieza best effort.
        }
    }
}