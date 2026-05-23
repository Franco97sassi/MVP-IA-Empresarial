namespace LocalMind.Api.Services.Mcp;

public record McpToolRequest(string ToolName, Dictionary<string, string> Parameters, int UserId);
public record McpToolResponse(string ServerName, string ToolName, bool Success, string Payload, long DurationMs);
public interface IMcpServer
{
    string Name { get; }
    IReadOnlyCollection<string> Tools { get; }
    Task<McpToolResponse> ExecuteAsync(McpToolRequest request, CancellationToken cancellationToken);
}

public interface IMcpHostService
{
    Task<IReadOnlyCollection<McpToolResponse>> ExecutePlanAsync(IEnumerable<McpToolRequest> plan, CancellationToken cancellationToken);
}
