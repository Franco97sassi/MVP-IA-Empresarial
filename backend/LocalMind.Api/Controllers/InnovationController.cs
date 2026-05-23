using LocalMind.Api.Services.FineTuning;
using LocalMind.Api.Services.Mcp;
using LocalMind.Api.Services.Multimodal;
using LocalMind.Api.Services.Orchestration;
using LocalMind.Api.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LocalMind.Api.Controllers;

[ApiController]
[Route("api/innovation")]
[Authorize]
public class InnovationController : ControllerBase
{
    [HttpPost("mcp/execute")]
    public async Task<IActionResult> ExecuteMcp([FromBody] List<McpToolRequestDto> plan, [FromServices] IMcpHostService host, CancellationToken cancellationToken)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var requests = plan.Select(item => new McpToolRequest(item.ToolName, item.Parameters ?? new(), userId));
        var result = await host.ExecutePlanAsync(requests, cancellationToken);
        return Ok(result);
    }

    [HttpPost("agents/orchestrate")]
    public async Task<IActionResult> Orchestrate([FromBody] AgentOrchestrationRequest request, [FromServices] IMultiAgentOrchestrator orchestrator, [FromServices] IOllamaService ollama, CancellationToken cancellationToken)
    {
        var result = await orchestrator.RunAsync(request.Message, (prompt, ct) => ollama.SendMessageAsync(prompt, ct), cancellationToken);
        return Ok(result);
    }

    [HttpPost("multimodal/stt")]
    public async Task<IActionResult> Stt(IFormFile file, [FromServices] IMultimodalService multimodal, CancellationToken cancellationToken)
    {
        var result = await multimodal.TranscribeAsync(file, cancellationToken);
        return Ok(result);
    }

    [HttpPost("multimodal/tts")]
    public async Task<IActionResult> Tts([FromBody] TtsRequest request, [FromServices] IMultimodalService multimodal, CancellationToken cancellationToken)
    {
        var result = await multimodal.SynthesizeAsync(request.Text, request.Voice ?? "alloy", cancellationToken);
        return Ok(result);
    }

    [HttpPost("fine-tuning/jobs")]
    public IActionResult CreateFtJob([FromBody] FineTuningJobRequest request, [FromServices] IFineTuningService fineTuning)
        => Ok(fineTuning.CreateJob(request));

    [HttpGet("fine-tuning/jobs")]
    public IActionResult ListFtJobs([FromServices] IFineTuningService fineTuning)
        => Ok(fineTuning.ListJobs());
}

public record McpToolRequestDto(string ToolName, Dictionary<string, string>? Parameters);
public record AgentOrchestrationRequest(string Message);
public record TtsRequest(string Text, string? Voice);
