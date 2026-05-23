namespace LocalMind.Api.Services.FineTuning;

public record FineTuningJobRequest(string Name, string BaseModel, int Epochs, int Seed);
public record FineTuningJobStatus(Guid Id, string Name, string BaseModel, string Status, DateTime CreatedAtUtc, Dictionary<string, double> Metrics);

public interface IFineTuningService
{
    FineTuningJobStatus CreateJob(FineTuningJobRequest request);
    IReadOnlyCollection<FineTuningJobStatus> ListJobs();
}

public class FineTuningService : IFineTuningService
{
    private static readonly List<FineTuningJobStatus> Jobs = [];
    public FineTuningJobStatus CreateJob(FineTuningJobRequest request)
    {
        var job = new FineTuningJobStatus(
            Guid.NewGuid(),
            request.Name,
            request.BaseModel,
            "created",
            DateTime.UtcNow,
            new Dictionary<string, double>
            {
                ["expected_grounding"] = 0.82,
                ["expected_toxicity"] = 0.03,
                ["epochs"] = request.Epochs,
                ["seed"] = request.Seed
            });
        Jobs.Add(job);
        return job;
    }

    public IReadOnlyCollection<FineTuningJobStatus> ListJobs() => Jobs.OrderByDescending(item => item.CreatedAtUtc).ToList();
}
