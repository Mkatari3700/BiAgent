using BISentinel.Api.Security;

namespace BISentinel.Api.Feedback;

public interface IReviewSubmissionStore
{
    Task<ReviewSubmission?> SubmitAsync(
        Guid reviewRunId,
        Guid submittedByUserId,
        IReadOnlyList<SubmitReviewFindingRequest> findings,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<ReviewSubmission?> GetMineAsync(
        Guid reviewRunId,
        Guid submittedByUserId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<ReviewSubmission?> GetAsync(
        Guid submissionId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<IReadOnlyList<ReviewSubmissionSummary>> GetQueueAsync(
        bool includeReviewed,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<IReadOnlyList<ReviewSubmissionSummary>> GetMyQueueAsync(
        Guid submittedByUserId,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);

    Task<ReviewSubmission?> CompleteReviewAsync(
        Guid submissionId,
        Guid reviewerUserId,
        IReadOnlyList<ReviewSubmissionFindingRequest> findings,
        CancellationToken cancellationToken,
        ActiveWorkspaceScope? scope = null);
}
