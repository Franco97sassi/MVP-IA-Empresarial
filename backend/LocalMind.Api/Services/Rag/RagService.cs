using System.Text.RegularExpressions;
using LocalMind.Api.Data;
using LocalMind.Api.DTOs.Documents;
using LocalMind.Api.Models;
using LocalMind.Api.Services.Ai;
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

    private static readonly HashSet<string> AllowedExtensions = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".txt",
        ".md"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(
        StringComparer.OrdinalIgnoreCase)
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
    private readonly RagOptions _options;

    public RagService(
        AppDbContext context,
        IOllamaService ollamaService,
        IDocumentTextExtractor textExtractor,
        ITextChunker chunker,
        IEmbeddingSerializer embeddingSerializer,
        IOptions<RagOptions> options)
    {
        _context = context;
        _ollamaService = ollamaService;
        _textExtractor = textExtractor;
        _chunker = chunker;
        _embeddingSerializer = embeddingSerializer;
        _options = options.Value;
    }

    public async Task<DocumentResponse> UploadDocumentAsync(
        int userId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(file);
        var currentDocuments = await _context.Documents.CountAsync(d => d.UserId == userId, cancellationToken);
        var currentStorage = await _context.Documents.Where(d => d.UserId == userId).SumAsync(d => (long?)d.SizeBytes, cancellationToken) ?? 0;

        if (currentDocuments >= _options.MaxDocumentsPerUser)
            throw new InvalidOperationException($"Lmite de documentos alcanzado ({_options.MaxDocumentsPerUser}).");

        if (currentStorage + file.Length > _options.MaxStorageBytesPerUser)
            throw new InvalidOperationException("Superaste el lmite de almacenamiento para tu cuenta.");
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

            var extractedText = await _textExtractor.ExtractTextAsync(
                file,
                cancellationToken);

            var chunks = _chunker.Split(
                extractedText,
                _options.ChunkSize,
                _options.ChunkOverlap);

            if (chunks.Count == 0)
            {
                throw new InvalidOperationException(
                    "No se pudo extraer texto útil del documento.");
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
                var embedding = await _ollamaService.GenerateEmbeddingAsync(
                    chunkContent,
                    cancellationToken);

                var chunkFileName = $"{chunkFilePrefix}-{index}.txt";
                var chunkFilePath = Path.Combine(chunksPath, chunkFileName);

                await File.WriteAllTextAsync(
                    chunkFilePath,
                    chunkContent,
                    cancellationToken);

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

        TryDeleteFile(storedFilePath);
        TryDeleteFiles(chunksPath, $"{chunkFilePrefix}-*.txt");

        return true;
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

        var minSimilarityScore =
            options?.MinSimilarityScore ?? _options.MinSimilarityScore;

        var maxRetrievedChunks = Math.Max(
            1,
            options?.MaxRetrievedChunks ?? _options.MaxRetrievedChunks);

        var chunksQuery = _context.DocumentChunks
            .AsNoTracking()
            .Include(chunk => chunk.Document)
            .Where(chunk => chunk.Document.UserId == userId);

        if (documentIds.Count > 0)
        {
            chunksQuery = chunksQuery.Where(
                chunk => documentIds.Contains(chunk.DocumentId));
        }

        var chunks = await chunksQuery.ToListAsync(cancellationToken);

        if (chunks.Count == 0)
        {
            return new RagSearchResult(Array.Empty<RagChunkMatch>());
        }

        var queryEmbedding = await _ollamaService.GenerateEmbeddingAsync(
            query,
            cancellationToken);

        var matches = chunks
            .Select(chunk => BuildMatch(query, queryEmbedding, chunk))
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

        return new RagEvaluationSummary(
            totalQuestions,
            questionsWithContext,
            expectedHits,
            totalQuestions > 0
                ? Math.Round((double)questionsWithContext / totalQuestions, 4)
                : 0,
            totalQuestions > 0
                ? Math.Round((double)expectedHits / totalQuestions, 4)
                : 0,
            Math.Round(averageTopScore, 4),
            items);
    }

    public static double CalculateKeywordScore(string query, string content)
    {
        var queryTokens = Tokenize(query)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var contentTokens = Tokenize(content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matches = queryTokens.Count(contentTokens.Contains);

        return (double)matches / queryTokens.Count;
    }

    public static double CalculateRankScore(
        double vectorScore,
        double keywordScore)
    {
        return (vectorScore * VectorWeight) + (keywordScore * KeywordWeight);
    }

    private RagChunkMatch BuildMatch(
        string query,
        IReadOnlyList<float> queryEmbedding,
        DocumentChunk chunk)
    {
        var chunkEmbedding = _embeddingSerializer.Deserialize(chunk.EmbeddingJson);

        var vectorScore = CosineSimilarity(queryEmbedding, chunkEmbedding);
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
            BuildPreview(chunk.Content));
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
            throw new InvalidOperationException(
                "Solo se permiten archivos PDF, TXT o MD.");
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) &&
            !AllowedContentTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException(
                "El tipo de contenido del archivo no es válido.");
        }
    }

    private string GetStorageRoot()
    {
        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, _options.StorageRoot));
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

    private static double CosineSimilarity(
        IReadOnlyList<float> first,
        IReadOnlyList<float> second)
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

        return Math.Max(
            0,
            dot / (Math.Sqrt(firstMagnitude) * Math.Sqrt(secondMagnitude)));
    }

    private static string BuildPreview(string content)
    {
        var normalized = string.Join(
            ' ',
            content.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

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