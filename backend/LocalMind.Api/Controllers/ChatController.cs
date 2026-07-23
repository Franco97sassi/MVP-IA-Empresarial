using System.Diagnostics;
using System.Security.Claims;
using LocalMind.Api.Data;
using LocalMind.Api.DTOs;
using LocalMind.Api.Models;
using LocalMind.Api.Services.Chat;
using LocalMind.Api.Services.Metrics;
using LocalMind.Api.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
namespace LocalMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private const int ConversationTitleMaxLength = 40;

    private readonly AppDbContext _context;
    private readonly IChatService _chatService;
    private readonly IMetricsService _metricsService;
    private readonly IInputSafetyService _inputSafetyService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        AppDbContext context,
        IChatService chatService,
        IMetricsService metricsService,
        IInputSafetyService inputSafetyService,
        IConfiguration configuration,
        ILogger<ChatController> logger)
    {
        _context = context;
        _chatService = chatService;
        _metricsService = metricsService;
        _inputSafetyService = inputSafetyService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendMessage(
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        _inputSafetyService.ValidateChatMessage(request.Message);

        var userId = GetUserId();
        var cleanMessage = _inputSafetyService.RedactSensitiveData(request.Message.Trim());
        var conversation = await GetOrCreateConversationAsync(
            userId,
            request.ConversationId,
            cleanMessage,
            cancellationToken);

        if (conversation is null)
        {
            return NotFound(new
            {
                message = "No se encontró la conversación."
            });
        }

        var stopwatch = Stopwatch.StartNew();
        ChatResult? chatResult = null;

        try
        {
            chatResult = await _chatService.GenerateResponseAsync(
                userId,
                cleanMessage,
                request.DocumentIds,
                conversation.Id,
                cancellationToken);

            await SaveMessagesAsync(
                conversation.Id,
                cleanMessage,
                chatResult.Response,
                cancellationToken);

            stopwatch.Stop();

            await RecordMetricAsync(
                userId,
                conversation.Id,
                cleanMessage,
                chatResult,
                stopwatch.ElapsedMilliseconds,
                error: null,
                cancellationToken);

            return Ok(BuildChatResponse(conversation.Id, chatResult));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            await RecordMetricAsync(
                userId,
                conversation.Id,
                cleanMessage,
                chatResult,
                stopwatch.ElapsedMilliseconds,
                ex.Message,
                CancellationToken.None);

            throw;
        }
    }
    [HttpPost("send-stream")]
    public async Task SendMessageStream(ChatRequest request, CancellationToken cancellationToken)
    {
        _inputSafetyService.ValidateChatMessage(request.Message);
        var userId = GetUserId();
        var cleanMessage = _inputSafetyService.RedactSensitiveData(request.Message.Trim());
        var conversation = await GetOrCreateConversationAsync(userId, request.ConversationId, cleanMessage, cancellationToken);
        if (conversation is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");

        var stopwatch = Stopwatch.StartNew();
        //var chatResult = await _chatService.GenerateResponseAsync(userId, cleanMessage, request.DocumentIds, cancellationToken);
        var chatResult = await _chatService.GenerateResponseAsync(userId, cleanMessage, request.DocumentIds, conversation.Id, cancellationToken);
        await WriteSseEventAsync("meta", JsonSerializer.Serialize(new
        {
            conversationId = conversation.Id,
            route = chatResult.Route,
            usedRag = chatResult.UsedRag,
            usedTool = chatResult.UsedTool,
            chunksUsed = chatResult.ChunksUsed,
            promptName = chatResult.PromptName,
            promptVersion = chatResult.PromptVersion
        }), cancellationToken);

        foreach (var chunk in ChunkText(chatResult.Response, 24))
        {
            await WriteSseEventAsync("chunk", chunk, cancellationToken);
       
           
        }

        await SaveMessagesAsync(conversation.Id, cleanMessage, chatResult.Response, cancellationToken);
        stopwatch.Stop();
        await RecordMetricAsync(userId, conversation.Id, cleanMessage, chatResult, stopwatch.ElapsedMilliseconds, null, cancellationToken);
        await WriteSseEventAsync("done", JsonSerializer.Serialize(BuildChatResponse(conversation.Id, chatResult)), cancellationToken);
    }

    private async Task WriteSseEventAsync(string eventName, string data, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {data}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static IEnumerable<string> ChunkText(string text, int chunkSize)
    {
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            yield return text.Substring(i, Math.Min(chunkSize, text.Length - i));
        }
    }
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var conversations = await _context.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.CreatedAt)
            .Select(conversation => new
            {
                conversation.Id,
                conversation.Title,
                conversation.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(conversations);
    }

    [HttpGet("history/{conversationId:int}")]
    public async Task<IActionResult> GetConversation(
        int conversationId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var conversation = await _context.Conversations
            .AsNoTracking()
            .Include(item => item.Messages)
            .FirstOrDefaultAsync(
                item => item.Id == conversationId && item.UserId == userId,
                cancellationToken);

        if (conversation is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            conversation.Id,
            conversation.Title,
            Messages = conversation.Messages
                .OrderBy(message => message.CreatedAt)
                .Select(message => new
                {
                    message.Role,
                    message.Content,
                    message.CreatedAt
                })
        });
    }
    [HttpPut("history/{conversationId:int}/title")]
    public async Task<IActionResult> RenameConversation(int conversationId, [FromBody] RenameConversationRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var title = request.Title?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { message = "El ttulo no puede estar vaco." });
        }

        if (title.Length > ConversationTitleMaxLength)
        {
            return BadRequest(new { message = $"El ttulo no puede superar los {ConversationTitleMaxLength} caracteres." });
        }

        var conversation = await _context.Conversations.FirstOrDefaultAsync(item => item.Id == conversationId && item.UserId == userId, cancellationToken);
        if (conversation is null) return NotFound();

        conversation.Title = title;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("history/{conversationId:int}")]
    public async Task<IActionResult> DeleteConversation(int conversationId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var conversation = await _context.Conversations
            .Include(item => item.Messages)
            .FirstOrDefaultAsync(item => item.Id == conversationId && item.UserId == userId, cancellationToken);

        if (conversation is null) return NotFound();

        _context.ChatMessages.RemoveRange(conversation.Messages);
        _context.Conversations.Remove(conversation);
        await _context.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpGet("history/{conversationId:int}/export")]
    public async Task<IActionResult> ExportConversation(int conversationId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var conversation = await _context.Conversations
            .AsNoTracking()
            .Include(item => item.Messages)
            .FirstOrDefaultAsync(item => item.Id == conversationId && item.UserId == userId, cancellationToken);

        if (conversation is null) return NotFound();

        var builder = new StringBuilder();
        builder.AppendLine($"# {conversation.Title}");
        builder.AppendLine();
        builder.AppendLine($"Creada: {conversation.CreatedAt:O}");
        builder.AppendLine();

        foreach (var message in conversation.Messages.OrderBy(m => m.CreatedAt))
        {
            builder.AppendLine($"## {message.Role.ToUpperInvariant()} - {message.CreatedAt:O}");
            builder.AppendLine(message.Content);
            builder.AppendLine();
        }

        return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/markdown", $"conversation-{conversation.Id}.md");
    }

    private async Task<Conversation?> GetOrCreateConversationAsync(
        int userId,
        int? conversationId,
        string firstMessage,
        CancellationToken cancellationToken)
    {
        if (conversationId is not null and > 0)
        {
            return await _context.Conversations
                .FirstOrDefaultAsync(
                    conversation =>
                        conversation.Id == conversationId &&
                        conversation.UserId == userId,
                    cancellationToken);
        }

        var conversation = new Conversation
        {
            UserId = userId,
            Title = BuildConversationTitle(firstMessage),
            CreatedAt = DateTime.UtcNow
        };

        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync(cancellationToken);

        return conversation;
    }

    private async Task SaveMessagesAsync(
        int conversationId,
        string userContent,
        string assistantContent,
        CancellationToken cancellationToken)
    {
        _context.ChatMessages.AddRange(
            new ChatMessage
            {
                ConversationId = conversationId,
                Role = "user",
                Content = userContent,
                CreatedAt = DateTime.UtcNow
            },
            new ChatMessage
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = assistantContent,
                CreatedAt = DateTime.UtcNow
            });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordMetricAsync(
        int userId,
        int conversationId,
        string userMessage,
        ChatResult? chatResult,
        long elapsedMs,
        string? error,
        CancellationToken cancellationToken)
    {
        try
        {
            await _metricsService.RecordAsync(new ChatMetricCreate
            {
                UserId = userId,
                ConversationId = conversationId,
                ModelUsed = _configuration["Ollama:Model"] ?? "qwen2.5-coder:7b",
                ResponseTimeMs = elapsedMs,
                ApproxTokens = (chatResult?.ApproxInputTokens ?? EstimateTokens(userMessage, null)) + EstimateTokens(string.Empty, chatResult?.Response),
                UsedRag = chatResult?.UsedRag ?? false,
                UsedTool = chatResult?.UsedTool ?? false,
                ToolName = chatResult?.ToolName,
                ChunksUsed = chatResult?.ChunksUsed ?? 0,
                Route = chatResult?.Route ?? "error",
                Error = error
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "No se pudo registrar la métrica de chat. Ejecutá las migraciones para crear la tabla ChatMetrics.");
        }
    }

    private static object BuildChatResponse(int conversationId, ChatResult chatResult)
    {
        return new
        {
            conversationId,
            response = chatResult.Response,
            usedRag = chatResult.UsedRag,
            usedTool = chatResult.UsedTool,
            toolName = chatResult.ToolName,
            route = chatResult.Route,
            chunksUsed = chatResult.ChunksUsed,
            sources = chatResult.Sources,
            promptName = chatResult.PromptName,
            promptVersion = chatResult.PromptVersion,
            approxInputTokens = chatResult.ApproxInputTokens
        };
    }

    private static string BuildConversationTitle(string message)
    {
        return message.Length > ConversationTitleMaxLength
            ? message[..ConversationTitleMaxLength]
            : message;
    }

    private static int EstimateTokens(string prompt, string? response)
    {
        var totalCharacters = prompt.Length + (response?.Length ?? 0);

        return Math.Max(1, (int)Math.Ceiling(totalCharacters / 4.0));
    }

    private int GetUserId()
    {
        return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }

    public sealed class RenameConversationRequest
    {
        public string Title { get; set; } = string.Empty;
    }
}