using System.Text;
using LocalMind.Api.Models;

namespace LocalMind.Api.Services.Tokens;

public interface ITokenBudgetService
{
    int EstimateTokens(string text);
    string TrimToApproxTokens(string text, int maxTokens);
    string BuildHistoryPrompt(string message, IReadOnlyCollection<ChatMessage> history, int maxTokens);
}

public class TokenBudgetService : ITokenBudgetService
{
    public int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    public string TrimToApproxTokens(string text, int maxTokens)
    {
        if (EstimateTokens(text) <= maxTokens)
        {
            return text;
        }

        var maxCharacters = Math.Max(0, maxTokens * 4);
        if (text.Length <= maxCharacters)
        {
            return text;
        }

        return text[^maxCharacters..].TrimStart();
    }

    public string BuildHistoryPrompt(string message, IReadOnlyCollection<ChatMessage> history, int maxTokens)
    {
        if (history.Count == 0)
        {
            return message;
        }

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Historial reciente de la conversación:");

        foreach (var item in history)
        {
            var role = item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? "Asistente"
                : "Usuario";
            promptBuilder.AppendLine($"{role}: {item.Content}");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Mensaje actual del usuario:");
        promptBuilder.AppendLine(message);

        return TrimToApproxTokens(promptBuilder.ToString(), maxTokens);
    }
}
