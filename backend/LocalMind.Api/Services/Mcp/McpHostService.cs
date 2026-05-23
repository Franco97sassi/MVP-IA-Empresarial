using System.Diagnostics;

namespace LocalMind.Api.Services.Mcp;

public class McpHostService : IMcpHostService
{
    private readonly IReadOnlyCollection<IMcpServer> _servers;

    public McpHostService(IEnumerable<IMcpServer> servers)
    {
        _servers = servers.ToList();
    }

    public async Task<IReadOnlyCollection<McpToolResponse>> ExecutePlanAsync(IEnumerable<McpToolRequest> plan, CancellationToken cancellationToken)
    {
        var responses = new List<McpToolResponse>();

        foreach (var request in plan)
        {
            var server = _servers.FirstOrDefault(item => item.Tools.Contains(request.ToolName, StringComparer.OrdinalIgnoreCase));
            if (server is null)
            {
                responses.Add(new McpToolResponse("none", request.ToolName, false, "Tool no disponible", 0));
                continue;
            }

            var sw = Stopwatch.StartNew();
            var result = await server.ExecuteAsync(request, cancellationToken);
            responses.Add(result with { DurationMs = sw.ElapsedMilliseconds });
        }

        return responses;
    }
}
