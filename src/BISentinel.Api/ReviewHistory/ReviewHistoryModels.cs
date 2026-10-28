using BISentinel.Api.Reviews;

namespace BISentinel.Api.ReviewHistory;

public sealed record ReviewHistorySummary(
    Guid ReviewRunId,
    string ProjectId,
    string ProjectName,
    DateTime ReviewedUtc,
    DateTime ExpiresUtc,
    int TablesChecked,
    int MeasuresChecked,
    int RelationshipsChecked,
    int ChecksRun,
    int FindingsCount);

public sealed record ReviewHistoryEntry(
    ReviewHistorySummary Summary,
    IReadOnlyList<ReviewHistoryFinding> Findings);

public sealed record ReviewHistoryFinding(
    Guid ReviewFindingId,
    string FindingFingerprint,
    string RuleId,
    string Severity,
    string Message,
    string Location,
    string Recommendation);

public sealed record SaveReviewHistoryResponse(
    Guid ReviewRunId,
    DateTime ReviewedUtc,
    DateTime ExpiresUtc,
    PbipReviewResult Review,
    IReadOnlyList<ReviewHistoryFinding> Findings);

public static class ReviewProjectName
{
    private static readonly System.Text.RegularExpressions.Regex VersionSuffix = new(
        @"^(?<name>.+) \(\d{8}-\d{6}-\d{3}(?:-\d+)?\)$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string GetBaseName(string projectName)
    {
        var match = VersionSuffix.Match(projectName);
        return match.Success ? match.Groups["name"].Value : projectName;
    }

    public static bool IsSameProject(string projectName, string baseName) =>
        string.Equals(GetBaseName(projectName), baseName, StringComparison.OrdinalIgnoreCase);
}
