using LocalMind.Api.Services.Metrics;
using LocalMind.Api.Services.Rag;

namespace LocalMind.Api.Services.Mcp;

public class DocumentSearchMcpServer : IMcpServer
{
    private readonly IRagService _ragService;
    public DocumentSearchMcpServer(IRagService ragService) => _ragService = ragService;
    public string Name => "DocumentSearch";
    public IReadOnlyCollection<string> Tools => ["document.search"];

    public async Task<McpToolResponse> ExecuteAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var query = request.Parameters.TryGetValue("query", out var q) ? q : string.Empty;
        var result = await _ragService.SearchAsync(request.UserId, query, new RagSearchOptions(), cancellationToken);
        var payload = $"matches:{result.Matches.Count};hasContext:{result.HasContext}";
        return new McpToolResponse(Name, request.ToolName, true, payload, 0);
    }
}

public class UserMetricsMcpServer : IMcpServer
{
    private readonly IMetricsService _metricsService;
    public UserMetricsMcpServer(IMetricsService metricsService) => _metricsService = metricsService;
    public string Name => "UserMetrics";
    public IReadOnlyCollection<string> Tools => ["metrics.summary"];

    public async Task<McpToolResponse> ExecuteAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var summary = await _metricsService.GetSummaryAsync(request.UserId, cancellationToken);
        var payload = $"total:{summary.TotalRequests};rag:{summary.RagRequests};tool:{summary.ToolRequests}";
        return new McpToolResponse(Name, request.ToolName, true, payload, 0);
    }
}

public class TaskExtractorMcpServer : IMcpServer
{
    public string Name => "TaskExtractor";
    public IReadOnlyCollection<string> Tools => ["task.extract"];

    public Task<McpToolResponse> ExecuteAsync(McpToolRequest request, CancellationToken cancellationToken)
    {
        var text = request.Parameters.TryGetValue("text", out var value) ? value : string.Empty;
        var tasks = text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Contains("debe", StringComparison.OrdinalIgnoreCase) || item.Contains("hacer", StringComparison.OrdinalIgnoreCase))
            .Take(5);
        var payload = string.Join(" | ", tasks);
        return Task.FromResult(new McpToolResponse(Name, request.ToolName, true, payload, 0));
    }
}
