namespace LocalMind.Api.Services.Multimodal;

public record SttResult(string Transcript, string Language);
public record TtsResult(string Voice, string Format, string Base64Audio);
public interface IMultimodalService
{
    Task<SttResult> TranscribeAsync(IFormFile file, CancellationToken cancellationToken);
    Task<TtsResult> SynthesizeAsync(string text, string voice, CancellationToken cancellationToken);
}

public class MultimodalService : IMultimodalService
{
    public async Task<SttResult> TranscribeAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var sample = await reader.ReadToEndAsync(cancellationToken);
        var transcript = string.IsNullOrWhiteSpace(sample)
            ? $"Transcripción simulada para archivo {file.FileName}"
            : sample[..Math.Min(300, sample.Length)];
        return new SttResult(transcript, "es");
    }

    public Task<TtsResult> SynthesizeAsync(string text, string voice, CancellationToken cancellationToken)
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"TTS({voice}): {text}"));
        return Task.FromResult(new TtsResult(voice, "txt-base64", payload));
    }
}
