using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
namespace LocalMind.Api.Services.Security;

public class InputSafetyService : IInputSafetyService
{
    private readonly ChatSecurityOptions _options;

    public InputSafetyService(IOptions<ChatSecurityOptions> options)
    {
        _options = options.Value;
    }

    public void ValidateChatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("El mensaje no puede estar vacío.");
        }

        if (message.Length > _options.MaxMessageLength)
        {
            throw new InvalidOperationException($"El mensaje supera el límite de {_options.MaxMessageLength} caracteres.");
        }

        var normalized = NormalizeForSafetyComparison(message);
        var isBlocked = _options.BlockedPromptPatterns.Any(pattern =>
            normalized.Contains(NormalizeForSafetyComparison(pattern), StringComparison.Ordinal));
        if (isBlocked)
        {
            throw new InvalidOperationException("El mensaje fue bloqueado por seguridad básica de prompt injection.");
        }
    }
    public string RedactSensitiveData(string value)
    {
        var redacted = value;
        foreach (var pattern in _options.SensitiveDataPatterns)
        {
            redacted = Regex.Replace(redacted, pattern, "[DATO_SENSIBLE]", RegexOptions.IgnoreCase);
        }

        return redacted;
    }
    private static string NormalizeForSafetyComparison(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
