namespace LocalMind.Api.Services.Prompts;

public record PromptRenderResult(string Name, string Version, string Content);

public interface IPromptTemplateService
{
    PromptRenderResult Render(string name, IReadOnlyDictionary<string, string>? variables = null);
}
