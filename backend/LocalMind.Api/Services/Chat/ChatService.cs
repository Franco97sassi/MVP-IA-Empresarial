using System.Text;
using LocalMind.Api.DTOs.Documents;
using LocalMind.Api.Services.Ai;
using LocalMind.Api.Services.Rag;
using LocalMind.Api.Services.Tools;
using LocalMind.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace LocalMind.Api.Services.Chat;

public class ChatService : IChatService
{
    private readonly IOllamaService _ollamaService;
    private readonly IRagService _ragService;
    private readonly IToolIntentDetector _toolIntentDetector;
    private readonly IAiToolService _aiToolService;
    private readonly AppDbContext _context;
    public ChatService(
        IOllamaService ollamaService,
        IRagService ragService,
        IToolIntentDetector toolIntentDetector,
        IAiToolService aiToolService)
    {
        _ollamaService = ollamaService;
        _ragService = ragService;
        _toolIntentDetector = toolIntentDetector;
        _aiToolService = aiToolService;
        _context = context;
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
                    Route = "tool"
                };
            }
        }

        if (!isDocumentQuestion)
        {
            return new ChatResult
            {
                Response = await _ollamaService.SendMessageAsync(
                                        //message,
                                        BuildPromptWithHistory(message, await LoadRecentHistoryAsync(userId, conversationId, cancellationToken)),

                    cancellationToken),
                UsedRag = false,
                Route = "chat"
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
            return new ChatResult
            {
                Response = await _ollamaService.SendMessageAsync(
                                        //message,
                                        BuildPromptWithHistory(message, await LoadRecentHistoryAsync(userId, conversationId, cancellationToken)),
                    cancellationToken),
                UsedRag = false,
                Route = "chat"
            };
        }

        var contextBuilder = new StringBuilder();

        foreach (var match in ragResult.Matches)
        {
            contextBuilder.AppendLine(
                $"Fuente: {match.FileName} | chunk {match.ChunkIndex} | relevancia {match.Score:P1}");

            contextBuilder.AppendLine(match.Content);
            contextBuilder.AppendLine("---");
        }

        const string systemPrompt =
            "Respondé siempre en español. Usá únicamente el contexto de documentos provisto cuando sea relevante. " +
            "Si el contexto no alcanza para responder, decilo claramente. Incluí una sección final llamada 'Fuentes' con el nombre del archivo y chunk usado.";

        var userPrompt =
            $"Contexto de documentos:\n{contextBuilder}\n\nPregunta del usuario:\n{message}";

        var response = await _ollamaService.SendMessageAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        return new ChatResult
        {
            Response = response,
            UsedRag = true,
            Route = "rag",
            ChunksUsed = ragResult.Matches.Count,
            Sources = ragResult.Matches.Select(match => new RagSourceResponse
            {
                DocumentId = match.DocumentId,
                FileName = match.FileName,
                ChunkIndex = match.ChunkIndex,
                Score = Math.Round(match.Score, 4),
                VectorScore = Math.Round(match.VectorScore, 4),
                KeywordScore = Math.Round(match.KeywordScore, 4),
                RankScore = Math.Round(match.RankScore, 4),
                Preview = match.Preview,
                ChunkReference = $"doc:{match.DocumentId}#chunk:{match.ChunkIndex}"
            }).ToList()
        };
    }
}