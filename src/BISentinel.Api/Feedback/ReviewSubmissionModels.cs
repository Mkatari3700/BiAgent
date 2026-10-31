namespace BISentinel.Api.Feedback;

public sealed record SubmitReviewRequest(IReadOnlyList<SubmitReviewFindingRequest> Findings);

public sealed record SubmitReviewFindingRequest(Guid ReviewFindingId, string? Justification);

public sealed record ReviewSubmissionFindingRequest(
    Guid ReviewFindingId,
    string Disposition,
    string? Response);

public sealed record CompleteReviewSubmissionRequest(
    IReadOnlyList<ReviewSubmissionFindingRequest> Findings);

public sealed record ReviewSubmissionFinding(
    Guid ReviewFindingId,
    string RuleId,
    string Severity,
    string Message,
    string Location,
    string Recommendation,
    string? Justification,
    string? ReviewerDisposition,
    string? ReviewerResponse);

public sealed record ReviewSubmission(
    Guid SubmissionId,
    Guid ReviewRunId,
    string ProjectName,
    string SubmittedBy,
    DateTimeOffset SubmittedUtc,
    string Status,
    string? ReviewedBy,
    DateTimeOffset? ReviewedUtc,
    IReadOnlyList<ReviewSubmissionFinding> Findings);

public sealed record ReviewSubmissionSummary(
    Guid SubmissionId,
    Guid ReviewRunId,
    string ProjectName,
    string SubmittedBy,
    DateTimeOffset SubmittedUtc,
    string Status,
    int FindingsCount);

public static class ReviewSubmissionValidator
{
    private static readonly HashSet<string> Dispositions =
        ["Accepted", "NeedsChanges", "Noted", "IncorrectFinding"];

    public static Dictionary<string, string[]> Validate(SubmitReviewRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Findings is null || request.Findings.Count == 0)
        {
            errors[nameof(request.Findings)] = ["Include the findings to submit."];
            return errors;
        }

        if (request.Findings.Any(item => item.ReviewFindingId == Guid.Empty) ||
            request.Findings.Select(item => item.ReviewFindingId).Distinct().Count() != request.Findings.Count)
        {
            errors[nameof(request.Findings)] = ["Finding IDs must be non-empty and unique."];
        }

        if (request.Findings.Any(item => item.Justification?.Length > 1000))
        {
            errors[nameof(request.Findings)] = ["Each justification must be no longer than 1000 characters."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(CompleteReviewSubmissionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Findings is null || request.Findings.Count == 0)
        {
            errors[nameof(request.Findings)] = ["Include a reviewer decision for every submitted finding."];
            return errors;
        }

        if (request.Findings.Any(item =>
                item.ReviewFindingId == Guid.Empty ||
                !Dispositions.Contains(item.Disposition)) ||
            request.Findings.Select(item => item.ReviewFindingId).Distinct().Count() != request.Findings.Count)
        {
            errors[nameof(request.Findings)] =
                ["Provide unique finding IDs and a supported reviewer disposition for each finding."];
        }

        if (request.Findings.Any(item => item.Response?.Length > 1000))
        {
            errors[nameof(request.Findings)] = ["Each reviewer response must be no longer than 1000 characters."];
        }

        return errors;
    }
}
