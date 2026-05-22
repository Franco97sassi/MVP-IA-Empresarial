using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
namespace LocalMind.Api.Services.Ai;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public Task<string> SendMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(
            "Respondé siempre en español.",
            message,
            cancellationToken
        );
    }

    public async Task<string> SendMessageAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var model = _configuration["Ollama:Model"] ?? "qwen2.5-coder:7b";
        var maxOutputTokens = GetConfiguredInt("Ollama:MaxOutputTokens", 512);
        var temperature = GetConfiguredDouble("Ollama:Temperature", 0.2);
        var keepAlive = _configuration["Ollama:KeepAlive"] ?? "10m";

        var payload = new
        {
            model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userMessage
                }
            },
            stream = false,
            keep_alive = keepAlive,
            options = new
            {
                num_predict = maxOutputTokens,
                temperature
            }
        };

        var json = JsonSerializer.Serialize(payload);

        Console.WriteLine("Enviando a Ollama:");
        Console.WriteLine(json);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        try
        {
            var response = await _httpClient.PostAsync(
                "/api/chat",
                content,
                cancellationToken
            );

            var responseJson = await response.Content.ReadAsStringAsync(
                cancellationToken
            );

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(responseJson);

            return document.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "No pude generar una respuesta.";
        }
        catch (TaskCanceledException ex)
        {
            return $"Ollama tardó más que el timeout configurado ({_httpClient.Timeout.TotalSeconds:N0} segundos). Probá con un modelo más chico, menos contexto o aumentá Ollama:RequestTimeoutSeconds. Detalle: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error al conectar con Ollama: {ex.Message}";
        }
    }
    public IAsyncEnumerable<string> StreamMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        return StreamMessageAsync("Respondé siempre en español.", message, cancellationToken);
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = _configuration["Ollama:Model"] ?? "qwen2.5-coder:7b";
        var maxOutputTokens = GetConfiguredInt("Ollama:MaxOutputTokens", 512);
        var temperature = GetConfiguredDouble("Ollama:Temperature", 0.2);
        var keepAlive = _configuration["Ollama:KeepAlive"] ?? "10m";

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            stream = true,
            keep_alive = keepAlive,
            options = new { num_predict = maxOutputTokens, temperature }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("message", out var messageElement) &&
                messageElement.TryGetProperty("content", out var contentElement))
            {
                var chunk = contentElement.GetString();
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }

            if (root.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean())
            {
                yield break;
            }
        }
    }

    public async Task<IReadOnlyList<float>> GenerateEmbeddingAsync(
     string text,
     CancellationToken cancellationToken = default)
    {
        var model = _configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        var payload = new
        {
            model,
            input = text
        };

        var json = JsonSerializer.Serialize(payload);

        Console.WriteLine("=== OLLAMA EMBEDDING REQUEST ===");
        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Text length: {text?.Length}");
        Console.WriteLine(text);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        var response = await _httpClient.PostAsync(
            "/api/embed",
            content,
            cancellationToken
        );

        var responseJson = await response.Content.ReadAsStringAsync(
            cancellationToken
        );

        Console.WriteLine("=== OLLAMA EMBEDDING RESPONSE ===");
        Console.WriteLine($"Status: {(int)response.StatusCode}");
        Console.WriteLine(responseJson);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("embeddings", out var embeddingsElement)
            && embeddingsElement.GetArrayLength() > 0)
        {
            return embeddingsElement[0]
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray();
        }

        if (root.TryGetProperty("embedding", out var embeddingElement))
        {
            return embeddingElement
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray();
        }

        throw new InvalidOperationException("Ollama no devolvió embeddings válidos.");
    }
    private int GetConfiguredInt(string key, int fallback)
    {
        return int.TryParse(_configuration[key], out var value)
            ? value
            : fallback;
    }

    private double GetConfiguredDouble(string key, double fallback)
    {
        return double.TryParse(
            _configuration[key],
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : fallback;
    }
}