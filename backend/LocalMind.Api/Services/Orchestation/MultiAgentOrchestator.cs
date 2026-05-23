namespace LocalMind.Api.Services.Orchestration;

public record AgentStep(string Agent, string Input, string Output, long DurationMs);
public record MultiAgentResult(string FinalAnswer, IReadOnlyCollection<AgentStep> Trace);

public interface IMultiAgentOrchestrator
{
    Task<MultiAgentResult> RunAsync(string userMessage, Func<string, CancellationToken, Task<string>> llm, CancellationToken cancellationToken);
}

public class MultiAgentOrchestrator : IMultiAgentOrchestrator
{
    public async Task<MultiAgentResult> RunAsync(string userMessage, Func<string, CancellationToken, Task<string>> llm, CancellationToken cancellationToken)
    {
        var trace = new List<AgentStep>();

        var plannerPrompt = $"Descomponé esta tarea en pasos accionables: {userMessage}";
        var plan = await RunAgentAsync("planner", plannerPrompt, llm, trace, cancellationToken);

        var researchPrompt = $"Investigá y resumí hallazgos del plan: {plan}";
        var research = await RunAgentAsync("researcher", researchPrompt, llm, trace, cancellationToken);

        var criticPrompt = $"Auditá calidad, riesgos y vacíos de este resumen: {research}";
        var critique = await RunAgentAsync("critic", criticPrompt, llm, trace, cancellationToken);

        var writerPrompt = $"Redactá la respuesta final para usuario. Plan:{plan}\nHallazgos:{research}\nCrítica:{critique}";
        var final = await RunAgentAsync("writer", writerPrompt, llm, trace, cancellationToken);

        return new MultiAgentResult(final, trace);
    }

    private static async Task<string> RunAgentAsync(string role, string prompt, Func<string, CancellationToken, Task<string>> llm, List<AgentStep> trace, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        var output = await llm(prompt, cancellationToken);
        trace.Add(new AgentStep(role, prompt, output, (long)(DateTime.UtcNow - start).TotalMilliseconds));
        return output;
    }
}
