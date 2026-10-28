namespace BISentinel.Api.Feedback;

public sealed record SaveReviewFeedbackRequest(
    Guid ReviewerId,
    Guid ReviewFindingId,
    string Rating,
    string? Comment);

public sealed record ReviewFeedbackEntry(
    Guid ReviewerId,
    Guid? ReviewFindingId,
    string? FindingFingerprint,
    string FeedbackStatus,
    string ProjectName,
    string RuleId,
    string Severity,
    string Message,
    string Location,
    string? Rating,
    string? Comment,
    DateTime UpdatedUtc);

public sealed record SaveReviewFeedbackResponse(bool Saved, DateTime UpdatedUtc);

public sealed record ReopenReviewFeedbackRequest(Guid ReviewerId);

public sealed record ReviewDashboardRun(
    Guid ReviewRunId,
    string ProjectName,
    DateTime ReviewedUtc,
    int FindingsCount,
    int ClosedFindingsCount,
    int WaitingFindingsCount,
    string FeedbackStatus);
