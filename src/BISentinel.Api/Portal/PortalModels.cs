namespace BISentinel.Api.Portal;

public sealed record PortalDashboardSummary(
    bool IsAdministrator,
    int ProjectReviewCount,
    int GovernanceRunCount,
    int OpenSupportRequestCount,
    int ActiveUserCount);

public sealed record CreateSupportRequest(
    string Category,
    string Subject,
    string Message);

public sealed record RespondToSupportRequest(
    string Response,
    string Status);

public sealed record SupportRequestRecord(
    Guid SupportRequestId,
    string Category,
    string Subject,
    string Message,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? AdminResponse,
    DateTimeOffset? RespondedUtc,
    string RequesterName,
    string? RequesterEmail,
    string? ResponderName);
