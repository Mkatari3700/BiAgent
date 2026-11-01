using BISentinel.Api.Security;

namespace BISentinel.Api.Feedback;

public interface IReviewFeedbackStore
{
    Task<DateTime?> SaveAsync(
        SaveReviewFeedbackRequest request,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<DateTime?> ReopenAsync(
        Guid reviewerId,
        Guid reviewFindingId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<DateTime?> ReopenRunAsync(
        Guid reviewerId,
        Guid reviewRunId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<IReadOnlyList<ReviewFeedbackEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<IReadOnlyList<ReviewFeedbackEntry>> GetForRunAsync(
        Guid reviewerId,
        Guid reviewRunId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<IReadOnlyList<ReviewDashboardRun>> GetDashboardAsync(
        Guid reviewerId,
        int limit,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);
}
