using System.Collections.ObjectModel;

namespace LocalMind.Api.Services.Prompts;

public class PromptTemplateService : IPromptTemplateService
{
    private static readonly IReadOnlyDictionary<string, (string Version, string Content)> Templates =
        new ReadOnlyDictionary<string, (string Version, string Content)>(
            new Dictionary<string, (string Version, string Content)>(StringComparer.OrdinalIgnoreCase)
            {
                ["chat.default"] = ("v1", "Respondé siempre en español."),
                ["rag.answer"] = ("v2", "Respondé siempre en español. Usá únicamente el contexto de documentos provisto cuando sea relevante. Si el contexto no alcanza para responder, decilo claramente. No inventes datos que no estén respaldados por el contexto. Incluí una sección final llamada 'Fuentes' con el nombre del archivo y chunk usado."),
                ["rag.no-context"] = ("v1", "Respondé en español. Si no hay evidencia documental suficiente, explicá que no encontraste contexto relevante y ofrecé reformular la pregunta o subir más documentos."),
                ["agent.planner"] = ("v1", "Descomponé esta tarea en pasos accionables: {{message}}"),
                ["agent.researcher"] = ("v1", "Investigá y resumí hallazgos del plan: {{plan}}"),
                ["agent.critic"] = ("v1", "Auditá calidad, riesgos y vacíos de este resumen: {{research}}"),
                ["agent.writer"] = ("v1", "Redactá la respuesta final para usuario. Plan: {{plan}}\nHallazgos: {{research}}\nCrítica: {{critique}}")
            });

    public PromptRenderResult Render(string name, IReadOnlyDictionary<string, string>? variables = null)
    {
        if (!Templates.TryGetValue(name, out var template))
        {
            throw new InvalidOperationException($"No existe el prompt '{name}'.");
        }

        var content = template.Content;
        if (variables is not null)
        {
            foreach (var variable in variables)
            {
                content = content.Replace($"{{{{{variable.Key}}}}}", variable.Value, StringComparison.Ordinal);
            }
        }

        return new PromptRenderResult(name, template.Version, content);
    }
}
