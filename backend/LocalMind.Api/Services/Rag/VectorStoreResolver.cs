using Microsoft.Extensions.Options;

namespace LocalMind.Api.Services.Rag;

public interface IVectorStoreResolver
{
    IVectorStore Resolve();
}

public class VectorStoreResolver : IVectorStoreResolver
{
    private readonly RagOptions _options;
    private readonly LocalVectorStore _localVectorStore;
    private readonly QdrantVectorStore _qdrantVectorStore;

    public VectorStoreResolver(
        IOptions<RagOptions> options,
        LocalVectorStore localVectorStore,
        QdrantVectorStore qdrantVectorStore)
    {
        _options = options.Value;
        _localVectorStore = localVectorStore;
        _qdrantVectorStore = qdrantVectorStore;
    }

    public IVectorStore Resolve()
    {
        return _options.VectorStoreProvider.Equals("Qdrant", StringComparison.OrdinalIgnoreCase)
            ? _qdrantVectorStore
            : _localVectorStore;
    }
}
