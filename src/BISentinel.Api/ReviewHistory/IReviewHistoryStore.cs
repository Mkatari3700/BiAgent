using BISentinel.Api.Reviews;

namespace BISentinel.Api.ReviewHistory;

public interface IReviewHistoryStore
{
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);

    Task<ReviewHistoryEntry> SaveAsync(
        PbipReviewResult review,
        int retentionDays,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewHistorySummary>> GetRecentAsync(
        int limit,
        string? projectName,
        CancellationToken cancellationToken);

    Task<ReviewHistoryEntry?> GetAsync(
        Guid reviewRunId,
        CancellationToken cancellationToken);
}
