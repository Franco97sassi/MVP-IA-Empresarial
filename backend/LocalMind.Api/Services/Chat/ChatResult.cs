using LocalMind.Api.DTOs.Documents;

namespace LocalMind.Api.Services.Chat;

public class ChatResult
{
    public string Response { get; set; } = string.Empty;

    public bool UsedRag { get; set; }
    public bool UsedTool { get; set; }

    public string? ToolName { get; set; }

    public string Route { get; set; } = "chat";

    public int ChunksUsed { get; set; }

    public IReadOnlyList<RagSourceResponse> Sources { get; set; } = Array.Empty<RagSourceResponse>();

    public string? PromptName { get; set; }

    public string? PromptVersion { get; set; }

    public int ApproxInputTokens { get; set; }
}
