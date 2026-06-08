using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMind.Api.Services.Rag;

public interface IEmbeddingCacheService
{
    Task<IReadOnlyList<float>> GetOrCreateAsync(string model, string text, Func<CancellationToken, Task<IReadOnlyList<float>>> factory, CancellationToken cancellationToken = default);
}

public class EmbeddingCacheService : IEmbeddingCacheService
{
    private readonly IMemoryCache _cache;

    public EmbeddingCacheService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<float>> GetOrCreateAsync(
        string model,
        string text,
        Func<CancellationToken, Task<IReadOnlyList<float>>> factory,
        CancellationToken cancellationToken = default)
    {
        var key = $"embedding:{model}:{Hash(text)}";
        if (_cache.TryGetValue<IReadOnlyList<float>>(key, out var cached))
        {
            return cached;
        }

        var embedding = await factory(cancellationToken);
        _cache.Set(key, embedding, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(30),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
            Size = Math.Max(1, embedding.Count)
        });

        return embedding;
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
