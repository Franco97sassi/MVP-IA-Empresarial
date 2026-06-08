namespace LocalMind.Api.Services.Tools;

public record ToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, string> JsonSchema,
    bool RequiresConfirmation = false);

public interface IToolDefinitionRegistry
{
    IReadOnlyCollection<ToolDefinition> List();
    ToolDefinition? Find(string name);
}

public class ToolDefinitionRegistry : IToolDefinitionRegistry
{
    private static readonly IReadOnlyList<ToolDefinition> Tools =
    [
        new ToolDefinition(
            "calculator",
            "Resuelve expresiones aritméticas seguras.",
            new Dictionary<string, string> { ["expression"] = "string" }),
        new ToolDefinition(
            "summarizeText",
            "Resume un texto pegado por el usuario.",
            new Dictionary<string, string> { ["text"] = "string" }),
        new ToolDefinition(
            "extractTasks",
            "Extrae tareas accionables desde un texto.",
            new Dictionary<string, string> { ["text"] = "string" }),
        new ToolDefinition(
            "generateStudyPlan",
            "Genera un plan de estudio a partir de páginas o días disponibles.",
            new Dictionary<string, string>
            {
                ["totalPages"] = "number",
                ["pagesPerDay"] = "number?",
                ["days"] = "number?"
            })
    ];

    public IReadOnlyCollection<ToolDefinition> List() => Tools;

    public ToolDefinition? Find(string name)
    {
        return Tools.FirstOrDefault(tool => tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
