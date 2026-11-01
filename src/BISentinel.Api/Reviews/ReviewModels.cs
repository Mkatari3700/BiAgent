namespace BISentinel.Api.Reviews;

public sealed class ReviewOptions
{
    public string PbiToolsPath { get; set; } = string.Empty;

    public string ProjectId { get; set; } = "default";

    public string MeasureTableName { get; set; } = "Measures";

    public string[] MeasureTableNameCandidates { get; set; } = [];

    public string TableNamePattern { get; set; } = "^[A-Z][A-Za-z0-9]*$";

    public string ColumnNamePattern { get; set; } = "^[A-Z][A-Za-z0-9]*$";

    public string MeasureNamePattern { get; set; } = "^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)*$";

    public string PlaceholderColumnPattern { get; set; } = "^Column\\d*$";

    public string FactTablePrefix { get; set; } = "Fact";

    public string DimensionTablePrefix { get; set; } = "Dim";

    public string[] GeneratedTablePrefixes { get; set; } = ["LocalDateTable_", "DateTableTemplate_"];

    public string[] RequiredMeasures { get; set; } = [];

    public bool RequireDivideForDivision { get; set; } = true;

    public bool WarnOnFormatInDax { get; set; } = true;

    public Dictionary<string, ReviewRulePolicy> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ReviewRulePolicy GetPolicy(string key) =>
        Rules.TryGetValue(key, out var policy) ? policy : new ReviewRulePolicy();
}

public sealed record ReviewRulePolicy
{
    /// <summary>Whether this built-in rule runs during a review.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Severity assigned to findings produced by the rule.</summary>
    public string Severity { get; init; } = "Warning";
}

public sealed record PbipProjectModel(
    string ProjectName,
    IReadOnlyList<string> RequiredFiles,
    IReadOnlyList<ModelTable> Tables,
    IReadOnlyList<ModelRelationship> Relationships,
    bool HasSemanticModel = true,
    IReadOnlyList<string>? ReportReferences = null)
{
    public IReadOnlyList<ModelReportFile> ReportFiles { get; init; } = [];
}

public sealed record ModelTable(
    string Name,
    string FileName,
    IReadOnlyList<ModelColumn> Columns,
    IReadOnlyList<ModelMeasure> Measures)
{
    public bool IsCalculated { get; init; }

    public IReadOnlyList<ModelPartition> Partitions { get; init; } = [];

    public IReadOnlyList<ModelHierarchy> Hierarchies { get; init; } = [];
}

public sealed record ModelColumn(
    string Name,
    string? DataType = null,
    bool IsCalculated = false,
    string? Expression = null,
    bool IsHidden = false);

public sealed record ModelMeasure(string Name, string Expression);

public sealed record ModelRelationship(
    string FromTable,
    string FromColumn,
    string ToTable,
    string ToColumn,
    bool IsBidirectional,
    string? FromCardinality = null,
    string? ToCardinality = null,
    bool? IsActive = null,
    string? CrossFilteringBehavior = null);

public sealed record ModelPartition(string Name, string? Mode, string? SourceType);

public sealed record ModelHierarchy(string Name, IReadOnlyList<string> Levels);

public sealed record ModelReportFile(string RelativePath, string Content);

public sealed record PbipReviewResult(
    string ProjectName,
    string ProjectId,
    int TablesChecked,
    int MeasuresChecked,
    int RelationshipsChecked,
    int ChecksRun,
    IReadOnlyList<PbipReviewFinding> Findings)
{
    public IReadOnlyList<string> ExecutedRuleKeys { get; init; } = [];

    public bool ReportReferencesAvailable { get; init; }
}

public sealed record PbipReviewFinding(
    string RuleId,
    string Severity,
    string Message,
    string Location,
    string Recommendation);

public sealed record ReviewRuleResult(int ChecksRun, IReadOnlyList<PbipReviewFinding> Findings);
