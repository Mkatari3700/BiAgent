namespace BISentinel.Api.GovernanceAudit;

public static class GovernanceExceptionApplicator
{
    public static GovernanceAuditResult Apply(
        GovernanceAuditResult result,
        IReadOnlyCollection<GovernanceExceptionRecord> exceptions,
        DateTimeOffset now)
    {
        var findings = result.Findings.Select(finding =>
        {
            var exception = exceptions
                .Where(candidate => Matches(finding, candidate))
                .OrderByDescending(candidate => candidate.ApprovedUtc)
                .FirstOrDefault();
            if (exception is null)
            {
                return finding;
            }

            var status = exception.Status == "Approved" && exception.ExpiresUtc > now
                ? "Approved"
                : exception.Status == "Approved" ? "Expired" : exception.Status;
            var isApproved = finding.Status == "FAIL" && status == "Approved";
            return finding with
            {
                Status = isApproved ? "EXCEPTION_APPROVED" : finding.Status,
                OriginalStatus = isApproved ? finding.Status : finding.OriginalStatus,
                ExceptionRecord = exception with { Status = status }
            };
        }).ToArray();

        return result with { Findings = findings };
    }

    private static bool Matches(
        GovernanceFinding finding,
        GovernanceExceptionRecord exception) =>
        finding.WorkspaceId == exception.WorkspaceId &&
        string.Equals(finding.Code, exception.CheckId, StringComparison.OrdinalIgnoreCase) &&
        (exception.ArtifactId is Guid artifactId
            ? finding.ArtifactId == artifactId
            : finding.ArtifactId is null &&
              string.Equals(finding.ArtifactName, exception.ArtifactName, StringComparison.OrdinalIgnoreCase) &&
              string.Equals(finding.ArtifactType ?? string.Empty, exception.ArtifactType ?? string.Empty, StringComparison.OrdinalIgnoreCase));
}
