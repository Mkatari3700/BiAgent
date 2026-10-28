namespace BISentinel.Api.Feedback;

public interface IReviewFeedbackStore
{
    Task<DateTime?> SaveAsync(SaveReviewFeedbackRequest request, CancellationToken cancellationToken);

    Task<DateTime?> ReopenAsync(
        Guid reviewerId,
        Guid reviewFindingId,
        CancellationToken cancellationToken);

    Task<DateTime?> ReopenRunAsync(
        Guid reviewerId,
        Guid reviewRunId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewFeedbackEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewFeedbackEntry>> GetForRunAsync(
        Guid reviewerId,
        Guid reviewRunId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewDashboardRun>> GetDashboardAsync(
        Guid reviewerId,
        int limit,
        CancellationToken cancellationToken);
}
