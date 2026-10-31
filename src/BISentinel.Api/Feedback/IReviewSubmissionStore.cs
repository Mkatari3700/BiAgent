namespace BISentinel.Api.Feedback;

public interface IReviewSubmissionStore
{
    Task<ReviewSubmission?> SubmitAsync(
        Guid reviewRunId,
        Guid submittedByUserId,
        IReadOnlyList<SubmitReviewFindingRequest> findings,
        CancellationToken cancellationToken);

    Task<ReviewSubmission?> GetMineAsync(
        Guid reviewRunId,
        Guid submittedByUserId,
        CancellationToken cancellationToken);

    Task<ReviewSubmission?> GetAsync(
        Guid submissionId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewSubmissionSummary>> GetQueueAsync(
        bool includeReviewed,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReviewSubmissionSummary>> GetMyQueueAsync(
        Guid submittedByUserId,
        CancellationToken cancellationToken);

    Task<ReviewSubmission?> CompleteReviewAsync(
        Guid submissionId,
        Guid reviewerUserId,
        IReadOnlyList<ReviewSubmissionFindingRequest> findings,
        CancellationToken cancellationToken);
}
