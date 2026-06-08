using System.Diagnostics;

namespace LocalMind.Api.Services.Observability;

public interface IAiTelemetryService
{
    IDisposable Measure(string operation, IReadOnlyDictionary<string, object?>? attributes = null);
}

public class AiTelemetryService : IAiTelemetryService
{
    private readonly ILogger<AiTelemetryService> _logger;

    public AiTelemetryService(ILogger<AiTelemetryService> logger)
    {
        _logger = logger;
    }

    public IDisposable Measure(string operation, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        return new Measurement(_logger, operation, attributes);
    }

    private sealed class Measurement : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _operation;
        private readonly IReadOnlyDictionary<string, object?>? _attributes;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public Measurement(ILogger logger, string operation, IReadOnlyDictionary<string, object?>? attributes)
        {
            _logger = logger;
            _operation = operation;
            _attributes = attributes;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _logger.LogInformation(
                "AI operation {Operation} completed in {ElapsedMs}ms with {@Attributes}",
                _operation,
                _stopwatch.ElapsedMilliseconds,
                _attributes ?? new Dictionary<string, object?>());
        }
    }
}
