using System.Runtime.CompilerServices;
using System.Text;
using LocalMind.Api.Data;
using LocalMind.Api.DTOs.Documents;
using LocalMind.Api.Models;
using LocalMind.Api.Services.Ai;
using LocalMind.Api.Services.Prompts;
using LocalMind.Api.Services.Rag;
using LocalMind.Api.Services.Tokens;
using LocalMind.Api.Services.Tools;
using Microsoft.EntityFrameworkCore;

namespace LocalMind.Api.Services.Chat;

public class ChatService : IChatService
{
    private readonly IOllamaService _ollamaService;
    private readonly IRagService _ragService;
    private readonly IToolIntentDetector _toolIntentDetector;
    private readonly IAiToolService _aiToolService;
    private readonly AppDbContext _context;
    private readonly IPromptTemplateService _promptTemplateService;
    private readonly ITokenBudgetService _tokenBudgetService;
    private readonly IConfiguration _configuration;

    public ChatService(
        IOllamaService ollamaService,
        IRagService ragService,
        IToolIntentDetector toolIntentDetector,
        IAiToolService aiToolService,
        AppDbContext context,
        IPromptTemplateService promptTemplateService,
        ITokenBudgetService tokenBudgetService,
        IConfiguration configuration)
    {
        _ollamaService = ollamaService;
        _ragService = ragService;
        _toolIntentDetector = toolIntentDetector;
        _aiToolService = aiToolService;
        _context = context;
        _promptTemplateService = promptTemplateService;
        _tokenBudgetService = tokenBudgetService;
        _configuration = configuration;
    }

    public async Task<ChatResult> GenerateResponseAsync(
        int userId,
        string message,
        IReadOnlyCollection<int>? documentIds = null,
        int? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        var toolIntent = _toolIntentDetector.Detect(message);
        var isDocumentQuestion = _toolIntentDetector.IsDocumentQuestion(message);

        if (toolIntent is not ToolIntent.None && !isDocumentQuestion)
        {
            var toolResult = await _aiToolService.TryExecuteAsync(
                toolIntent,
                message,
                cancellationToken);

            if (toolResult is not null)
            {
                return new ChatResult
                {
                    Response = toolResult.Response,
                    UsedTool = true,
                    ToolName = toolResult.ToolName,
                    Route = "tool",
                    ApproxInputTokens = _tokenBudgetService.EstimateTokens(message)
                };
            }
        }

        var history = await LoadRecentHistoryAsync(userId, conversationId, cancellationToken);
        var maxHistoryTokens = GetConfiguredInt("Ollama:MaxHistoryTokens", 1200);

        if (!isDocumentQuestion)
        {
            var prompt = _promptTemplateService.Render("chat.default");
            var userPrompt = _tokenBudgetService.BuildHistoryPrompt(
                message,
                history,
                maxHistoryTokens);

            return new ChatResult
            {
                Response = await _ollamaService.SendMessageAsync(
                    prompt.Content,
                    userPrompt,
                    cancellationToken),

                UsedRag = false,
                Route = "chat",
                PromptName = prompt.Name,
                PromptVersion = prompt.Version,
                ApproxInputTokens = _tokenBudgetService.EstimateTokens(prompt.Content + userPrompt)
            };
        }

        var ragResult = await _ragService.SearchAsync(
            userId,
            message,
            new RagSearchOptions
            {
                DocumentIds = documentIds ?? Array.Empty<int>()
            },
            cancellationToken);

        if (!ragResult.HasContext)
        {
            var noContextPrompt = _promptTemplateService.Render("rag.no-context");
            var userPrompt = _tokenBudgetService.BuildHistoryPrompt(
                message,
                history,
                maxHistoryTokens);

            return new ChatResult
            {
                Response = await _ollamaService.SendMessageAsync(
                    noContextPrompt.Content,
                    userPrompt,
                    cancellationToken),

                UsedRag = false,
                Route = "chat",
                PromptName = noContextPrompt.Name,
                PromptVersion = noContextPrompt.Version,
                ApproxInputTokens = _tokenBudgetService.EstimateTokens(noContextPrompt.Content + userPrompt)
            };
        }

        var contextBuilder = BuildRagContext(ragResult.Matches);
        var ragPrompt = _promptTemplateService.Render("rag.answer");

        var ragUserPrompt =
            $"Contexto de documentos:\n{contextBuilder}\n\nPregunta del usuario:\n{message}";

        var response = await _ollamaService.SendMessageAsync(
            ragPrompt.Content,
            ragUserPrompt,
            cancellationToken);

        var sources = BuildSources(ragResult.Matches);

        return new ChatResult
        {
            Response = EnsureSourcesSection(response, sources),
            UsedRag = true,
            Route = "rag",
            ChunksUsed = ragResult.Matches.Count,
            Sources = sources,
            PromptName = ragPrompt.Name,
            PromptVersion = ragPrompt.Version,
            ApproxInputTokens = _tokenBudgetService.EstimateTokens(ragPrompt.Content + ragUserPrompt)
        };
    }

    public async IAsyncEnumerable<string> StreamResponseAsync(
     int userId,
     string message,
     IReadOnlyCollection<int>? documentIds = null,
     int? conversationId = null,
     [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var toolIntent = _toolIntentDetector.Detect(message);
        var isDocumentQuestion = _toolIntentDetector.IsDocumentQuestion(message);

        if (toolIntent is not ToolIntent.None && !isDocumentQuestion)
        {
            var fallback = await GenerateResponseAsync(
                userId,
                message,
                documentIds,
                conversationId,
                cancellationToken);

            foreach (var chunk in fallback.Response.Chunk(24))
            {
                yield return new string(chunk);
            }

            yield break;
        }

        var history = await LoadRecentHistoryAsync(userId, conversationId, cancellationToken);
        var maxHistoryTokens = GetConfiguredInt("Ollama:MaxHistoryTokens", 1200);

        if (!isDocumentQuestion)
        {
            var chatPrompt = _promptTemplateService.Render("chat.default");
            var chatUserPrompt = _tokenBudgetService.BuildHistoryPrompt(
                message,
                history,
                maxHistoryTokens);

            await foreach (var chunk in _ollamaService.StreamMessageAsync(
                chatPrompt.Content,
                chatUserPrompt,
                cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        var ragResult = await _ragService.SearchAsync(
            userId,
            message,
            new RagSearchOptions
            {
                DocumentIds = documentIds ?? Array.Empty<int>()
            },
            cancellationToken);

        if (!ragResult.HasContext)
        {
            var noContextPrompt = _promptTemplateService.Render("rag.no-context");
            var noContextUserPrompt = _tokenBudgetService.BuildHistoryPrompt(
                message,
                history,
                maxHistoryTokens);

            await foreach (var chunk in _ollamaService.StreamMessageAsync(
                noContextPrompt.Content,
                noContextUserPrompt,
                cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        var contextBuilder = BuildRagContext(ragResult.Matches);
        var ragPrompt = _promptTemplateService.Render("rag.answer");
        var ragUserPrompt =
            $"Contexto de documentos:\n{contextBuilder}\n\nPregunta del usuario:\n{message}";

        await foreach (var chunk in _ollamaService.StreamMessageAsync(
            ragPrompt.Content,
            ragUserPrompt,
            cancellationToken))
        {
            yield return chunk;
        }

        var sources = BuildSources(ragResult.Matches);
        var sourceSection = EnsureSourcesSection(string.Empty, sources);

        if (!string.IsNullOrWhiteSpace(sourceSection))
        {
            yield return $"\n\n{sourceSection}";
        }
    }
    private async Task<List<ChatMessage>> LoadRecentHistoryAsync(
        int userId,
        int? conversationId,
        CancellationToken cancellationToken)
    {
        if (!conversationId.HasValue)
        {
            return [];
        }

        return await _context.ChatMessages
            .AsNoTracking()
            .Where(message =>
                message.ConversationId == conversationId.Value &&
                message.Conversation.UserId == userId)
            .OrderByDescending(message => message.CreatedAt)
            .Take(12)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private static string BuildRagContext(IReadOnlyCollection<RagChunkMatch> matches)
    {
        var builder = new StringBuilder();

        foreach (var match in matches)
        {
            builder.AppendLine(
                $"Fuente: {match.FileName} | chunk {match.ChunkIndex} | relevancia {match.Score:P1} | proveedor {match.RetrievalProvider}");

            builder.AppendLine(match.Content);
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    private static List<RagSourceResponse> BuildSources(IReadOnlyCollection<RagChunkMatch> matches)
    {
        return matches.Select(match => new RagSourceResponse
        {
            DocumentId = match.DocumentId,
            FileName = match.FileName,
            ChunkIndex = match.ChunkIndex,
            Score = Math.Round(match.Score, 4),
            VectorScore = Math.Round(match.VectorScore, 4),
            KeywordScore = Math.Round(match.KeywordScore, 4),
            RankScore = Math.Round(match.RankScore, 4),
            Preview = match.Preview,
            ChunkReference = $"doc:{match.DocumentId}#chunk:{match.ChunkIndex}",
            RetrievalProvider = match.RetrievalProvider
        }).ToList();
    }

    private static string EnsureSourcesSection(
        string response,
        IReadOnlyCollection<RagSourceResponse> sources)
    {
        if (sources.Count == 0 || response.Contains("Fuentes", StringComparison.OrdinalIgnoreCase))
        {
            return response;
        }

        var builder = new StringBuilder(response.TrimEnd());

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Fuentes:");

        foreach (var source in sources)
        {
            builder.AppendLine($"- {source.FileName} | chunk {source.ChunkIndex}");
        }

        return builder.ToString().TrimEnd();
    }

    private int GetConfiguredInt(string key, int fallback)
    {
        return int.TryParse(_configuration[key], out var value)
            ? value
            : fallback;
    }
}