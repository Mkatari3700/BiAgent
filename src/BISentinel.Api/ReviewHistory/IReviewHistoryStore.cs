using BISentinel.Api.Reviews;
using BISentinel.Api.Security;

namespace BISentinel.Api.ReviewHistory;

public interface IReviewHistoryStore
{
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);

    Task<ReviewHistoryEntry> SaveAsync(
        PbipReviewResult review,
        int retentionDays,
        Guid uploadedByUserId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<IReadOnlyList<ReviewHistorySummary>> GetRecentAsync(
        int limit,
        string? projectName,
        Guid? uploadedByUserId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<ReviewHistoryEntry?> GetAsync(
        Guid reviewRunId,
        Guid? uploadedByUserId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);
}
