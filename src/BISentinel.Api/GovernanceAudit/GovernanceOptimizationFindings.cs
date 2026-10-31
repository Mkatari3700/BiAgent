using System.Text.Json;
using System.Text.RegularExpressions;

namespace BISentinel.Api.GovernanceAudit;

public sealed record GovernanceModelTable(
    string Name,
    IReadOnlyList<GovernanceModelColumn>? Columns,
    IReadOnlyList<GovernanceModelMeasure>? Measures);

public sealed record GovernanceModelColumn(string Name, bool? IsKey);

public sealed record GovernanceModelMeasure(string Name, string? Expression);

public static partial class GovernanceOptimizationFindings
{
    private const string Owner = "BI Governance Team";

    public static IReadOnlyList<GovernanceFinding> CreateFromPowerBiMetadata(
        Guid workspaceId,
        string workspaceName,
        Guid? datasetId,
        string? datasetName,
        IReadOnlyList<GovernanceModelTable>? tables,
        string? metadataError,
        IEnumerable<string> selectedCheckIds,
        DateTimeOffset evidenceTimestamp,
        IReadOnlyList<PowerBiReportFieldReference>? reportReferences = null,
        bool reportUsageComplete = false,
        int reportsExpected = 0,
        int reportsScanned = 0,
        IReadOnlyList<string>? reportUsageErrors = null)
    {
        var findings = new List<GovernanceFinding>();
        foreach (var checkId in selectedCheckIds
                     .Where(id => id.StartsWith("OPT-", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!GovernanceCheckCatalog.TryGet(checkId, out var check))
            {
                continue;
            }

            if (tables is null || tables.Count == 0)
            {
                findings.Add(CreateFinding(
                    check,
                    workspaceId,
                    workspaceName,
                    datasetId,
                    datasetName,
                    null,
                    "INSUFFICIENT_EVIDENCE",
                    metadataError ??
                        "Power BI returned no model table metadata. This is not evidence that the model has no unused objects.",
                    evidenceTimestamp,
                    new
                    {
                        datasetId,
                        datasetName,
                        metadataAvailable = false,
                        metadataError,
                        evidenceTimestamp
                    }));
                continue;
            }

            if (!reportUsageComplete)
            {
                findings.Add(CreateFinding(
                    check,
                    workspaceId,
                    workspaceName,
                    datasetId,
                    datasetName,
                    null,
                    "INSUFFICIENT_EVIDENCE",
                    BuildReportCoverageDetail(reportsExpected, reportsScanned, reportUsageErrors),
                    evidenceTimestamp,
                    new
                    {
                        datasetId,
                        datasetName,
                        tableCount = tables.Count,
                        columnCount = tables.Sum(table => table.Columns?.Count ?? 0),
                        measureCount = tables.Sum(table => table.Measures?.Count ?? 0),
                        reportsExpected,
                        reportsScanned,
                        reportUsageComplete,
                        reportUsageErrors = reportUsageErrors ?? [],
                        evidenceTimestamp
                    }));
                continue;
            }

            var measures = tables.SelectMany(table => table.Measures ?? [])
                .ToArray();
            var measureExpressions = measures
                .Select(measure => measure.Expression ?? string.Empty)
                .ToArray();
            var unusedTables = FindUnusedTables(tables, measureExpressions, reportReferences ?? []);
            var unusedColumns = FindUnusedColumns(tables, measureExpressions, reportReferences ?? []);
            var unusedMeasures = FindUnusedMeasures(tables, reportReferences ?? []);
            var objectCounts = new Dictionary<string, int>
            {
                ["tableCount"] = tables.Count,
                ["potentiallyUnusedTableCount"] = unusedTables.Count,
                ["columnCount"] = tables.Sum(table => table.Columns?.Count ?? 0),
                ["eligibleColumnCount"] = tables.Sum(table =>
                    (table.Columns ?? []).Count(column => column.IsKey != true)),
                ["potentiallyUnusedColumnCount"] = unusedColumns.Count,
                ["measureCount"] = measures.Length,
                ["potentiallyUnusedMeasureCount"] = unusedMeasures.Count
            };
            var checkIdUpper = checkId.ToUpperInvariant();
            var candidates = checkIdUpper switch
            {
                "OPT-001" => unusedTables,
                "OPT-002" => unusedColumns,
                "OPT-003" => unusedMeasures,
                _ => []
            };
            var (objectLabel, objectTotal, unusedCount) = checkIdUpper switch
            {
                "OPT-001" => ("tables", tables.Count, unusedTables.Count),
                "OPT-002" => ("eligible columns", objectCounts["eligibleColumnCount"], unusedColumns.Count),
                "OPT-003" => ("measures", measures.Length, unusedMeasures.Count),
                _ => ("objects", 0, 0)
            };

            if (candidates.Count == 0)
            {
                findings.Add(CreateFinding(
                    check,
                    workspaceId,
                    workspaceName,
                    datasetId,
                    datasetName,
                    null,
                    "PASS",
                    $"No potentially unused {objectLabel} were detected ({unusedCount} of {objectTotal}) in {reportsScanned} scanned report definition(s) in this workspace. This result does not cover reports in other workspaces or external consumers.",
                    evidenceTimestamp,
                    new
                    {
                        datasetId,
                        datasetName,
                        objectCounts,
                        reportReferencesAvailable = true,
                        reportsExpected,
                        reportsScanned,
                        evaluation = "Compared model metadata and DAX dependencies with visual field references in every selected report definition in this workspace. External consumers are not covered.",
                        evidenceTimestamp
                    },
                    actualValue: $"{unusedCount} of {objectTotal} {objectLabel} potentially unused"));
                continue;
            }

            foreach (var candidate in candidates)
            {
                findings.Add(CreateFinding(
                    check,
                    workspaceId,
                    workspaceName,
                    datasetId,
                    datasetName,
                    candidate.Name,
                    "FAIL",
                    $"{unusedCount} of {objectTotal} {objectLabel} may be unused. {candidate.Kind} {candidate.Name} has no detected reference in DAX dependencies or visual fields of {reportsScanned} scanned report definition(s) in this workspace. Reports in other workspaces and external consumers are not covered; verify them before making changes.",
                    evidenceTimestamp,
                    new
                    {
                        datasetId,
                        datasetName,
                        artifactType = candidate.Kind,
                        artifactName = candidate.Name,
                        objectCounts,
                        reportReferencesAvailable = true,
                        reportsExpected,
                        reportsScanned,
                        evaluation = "Static DAX/model metadata and selected-workspace report visual analysis. Candidate is advisory and does not prove there are no external consumers.",
                        evidenceTimestamp
                    },
                    candidate.Kind,
                    $"{unusedCount} of {objectTotal} {objectLabel} potentially unused"));
            }
        }

        return findings;
    }

    private static IReadOnlyList<(string Kind, string Name)> FindUnusedTables(
        IReadOnlyList<GovernanceModelTable> tables,
        IReadOnlyList<string> measureExpressions,
        IReadOnlyList<PowerBiReportFieldReference> reportReferences) =>
        tables
            .Where(table =>
                !measureExpressions.Any(expression => ReferencesTable(expression, table.Name)) &&
                !reportReferences.Any(reference =>
                    string.Equals(reference.TableName, table.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(table => (Kind: "Table", Name: table.Name))
            .ToArray();

    private static IReadOnlyList<(string Kind, string Name)> FindUnusedColumns(
        IReadOnlyList<GovernanceModelTable> tables,
        IReadOnlyList<string> measureExpressions,
        IReadOnlyList<PowerBiReportFieldReference> reportReferences) =>
        tables
            .SelectMany(table => (table.Columns ?? [])
                .Where(column => column.IsKey != true &&
                    !measureExpressions.Any(expression =>
                        ReferencesColumn(expression, table.Name, column.Name)) &&
                    !reportReferences.Any(reference =>
                        string.Equals(reference.TableName, table.Name, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(reference.FieldName, column.Name, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(reference.Kind, "Measure", StringComparison.OrdinalIgnoreCase)))
                .Select(column => (Kind: "Column", Name: $"{table.Name}[{column.Name}]")))
            .ToArray();

    private static IReadOnlyList<(string Kind, string Name)> FindUnusedMeasures(
        IReadOnlyList<GovernanceModelTable> tables,
        IReadOnlyList<PowerBiReportFieldReference> reportReferences)
    {
        var measures = tables
            .SelectMany(table => (table.Measures ?? [])
                .Select(measure => (Table: table.Name, Measure: measure)))
            .ToArray();
        return measures
            .Where(item => !measures.Any(other =>
                    (!string.Equals(other.Table, item.Table, StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(other.Measure.Name, item.Measure.Name, StringComparison.OrdinalIgnoreCase)) &&
                    ReferencesMeasure(other.Measure.Expression ?? string.Empty, item.Measure.Name)) &&
                !reportReferences.Any(reference =>
                    (reference.TableName is null ||
                     string.Equals(reference.TableName, item.Table, StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(reference.FieldName, item.Measure.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(item => (Kind: "Measure", Name: $"{item.Table}[{item.Measure.Name}]"))
            .ToArray();
    }

    private static string BuildReportCoverageDetail(
        int reportsExpected,
        int reportsScanned,
        IReadOnlyList<string>? reportUsageErrors)
    {
        if (reportsExpected < 0)
        {
            return "Reports were not selected for this scan. Select Reports and Semantic models to compare model objects with report visuals; no model objects are marked unused without report coverage.";
        }

        var detail = $"Report visual usage could not be fully evaluated: {reportsScanned} of {reportsExpected} linked report definitions were scanned. No model objects are marked unused when report coverage is incomplete.";
        if (reportUsageErrors is { Count: > 0 })
        {
            detail += $" Report export/parse issues: {string.Join("; ", reportUsageErrors.Take(3))}.";
        }

        return detail;
    }

    private static bool ReferencesTable(string expression, string tableName) =>
        Regex.IsMatch(
            expression,
            $@"(?<!\w){Regex.Escape(tableName)}(?!\w)",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

    private static bool ReferencesColumn(string expression, string tableName, string columnName) =>
        Regex.IsMatch(
            expression,
            $@"(?:'{Regex.Escape(tableName.Replace("'", "''"))}'|{Regex.Escape(tableName)})\s*\[\s*{Regex.Escape(columnName)}\s*\]",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100)) ||
        BracketedNameRegex().Matches(expression).Any(match =>
            match.Index > 0 &&
            expression[..match.Index].TrimEnd().EndsWith($"'{tableName}'", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(match.Groups["name"].Value, columnName, StringComparison.OrdinalIgnoreCase));

    private static bool ReferencesMeasure(string expression, string measureName) =>
        BracketedNameRegex().Matches(expression).Any(match =>
            string.Equals(match.Groups["name"].Value, measureName, StringComparison.OrdinalIgnoreCase));

    private static GovernanceFinding CreateFinding(
        GovernanceCheckDefinition check,
        Guid workspaceId,
        string workspaceName,
        Guid? datasetId,
        string? datasetName,
        string? artifactName,
        string status,
        string detail,
        DateTimeOffset evidenceTimestamp,
        object evidence,
        string? artifactType = null,
        string? actualValue = null) =>
        new(
            check.CheckId,
            check.Severity,
            workspaceId,
            workspaceName,
            null,
            artifactType ?? "Semantic model",
            artifactName ?? datasetName,
            check.Name,
            detail,
            check.Version,
            check.Name,
            Owner,
            check.EvidenceSource,
            evidenceTimestamp,
            status,
            check.EvaluationLogic,
            check.ExceptionRules,
            check.ExpectedResult,
            actualValue ?? (status switch
            {
                "FAIL" => "Potential unused model object detected",
                "PASS" => "No unused model objects detected within the evaluated scope",
                _ => "Model metadata or report usage evidence unavailable or incomplete"
            }),
            JsonSerializer.SerializeToElement(evidence));

    [GeneratedRegex(@"(?<!\w)\[(?<name>[^\]]+)\]")]
    private static partial Regex BracketedNameRegex();
}
