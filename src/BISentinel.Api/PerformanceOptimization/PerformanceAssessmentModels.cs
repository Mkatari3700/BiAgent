namespace BISentinel.Api.PerformanceOptimization;

public sealed record PerformanceAssessment(
    string ProjectName,
    string ArtifactType,
    DateTimeOffset AnalyzedAt,
    PerformanceExecutiveSummary ExecutiveSummary,
    PerformanceScope ScopeAnalyzed,
    PerformanceModelInventory ModelInventory,
    IReadOnlyList<PerformanceReportInventory> ReportInventory,
    IReadOnlyList<PerformanceDaxInventory> DaxInventory,
    IReadOnlyList<PerformanceReusableArtifact> ReusableArtifacts,
    IReadOnlyList<PerformanceFinding> Findings,
    PerformanceEvidenceSeparation EvidenceSeparation,
    IReadOnlyList<PerformanceOpportunity> PotentialOptimizationCandidates,
    IReadOnlyList<PerformanceEvidenceGap> EvidenceGaps,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> RecommendedNextSteps,
    FabricReadiness FabricReadiness,
    IReadOnlyList<PerformanceGate> AcceptanceGates);

public sealed record PerformanceExecutiveSummary(
    int ConfirmedStaticFindings,
    int Hypotheses,
    int MeasuredIssues,
    string PerformanceConclusion);

public sealed record PerformanceScope(
    bool SemanticModelIdentified,
    bool ReportDefinitionsFound,
    int Reports,
    int Tables,
    int Columns,
    int Measures,
    int Relationships,
    int Partitions,
    int Pages,
    int Visuals,
    string AnalysisBoundary);

public sealed record PerformanceModelInventory(
    IReadOnlyList<string> SemanticModels,
    IReadOnlyList<PerformanceTableInventory> Tables,
    IReadOnlyList<PerformanceRelationshipInventory> Relationships,
    int CalculatedColumns,
    int CalculatedTables,
    int Hierarchies,
    IReadOnlyList<string> ModelDesignObservations,
    string IncrementalRefreshReadiness);

public sealed record PerformanceTableInventory(
    string Name,
    string Artifact,
    int Columns,
    IReadOnlyList<PerformanceColumnInventory> ColumnInventory,
    int Measures,
    int CalculatedColumns,
    bool IsCalculatedTable,
    IReadOnlyList<string> Partitions,
    IReadOnlyList<string> Hierarchies);

public sealed record PerformanceColumnInventory(
    string Name,
    string? DataType,
    bool IsCalculated,
    bool IsHidden,
    string? Expression);

public sealed record PerformanceRelationshipInventory(
    string From,
    string To,
    string? FromCardinality,
    string? ToCardinality,
    bool? IsActive,
    string? FilterDirection,
    string Evidence);

public sealed record PerformanceReportInventory(
    string ReportName,
    string PageName,
    int VisualCount,
    int SlicerCount,
    int? CustomVisualCount,
    bool? HasBookmarks,
    bool? HasDrillthrough,
    bool? HasTooltipPages,
    bool? HasConditionalFormatting,
    bool? HasCrossFilteringConfiguration,
    IReadOnlyList<string> PotentialBottlenecks,
    IReadOnlyList<string> Opportunities,
    string Evidence);

public sealed record PerformanceDaxInventory(
    string TableName,
    string MeasureName,
    string Expression,
    string ObjectType,
    IReadOnlyList<PerformanceDaxIssue> Issues);

public sealed record PerformanceDaxIssue(
    string Severity,
    string Issue,
    string TechnicalExplanation,
    string RecommendedRewritePattern,
    string RiskOfChange,
    string Confidence,
    string EvidenceType);

public sealed record PerformanceReusableArtifact(
    string ArtifactType,
    string Name,
    string Location,
    string Evidence);

public sealed record PerformanceFinding(
    string Finding,
    string Impact,
    string Evidence,
    string Recommendation,
    string Priority,
    string ExpectedBenefit,
    string EvidenceType,
    string Confidence,
    string Category,
    string? OptimizationTag = null,
    string? Severity = null,
    string? RiskOfChange = null);

public sealed record PerformanceEvidenceSeparation(
    IReadOnlyList<PerformanceFinding> MeasuredIssues,
    IReadOnlyList<PerformanceFinding> PotentialIssues,
    IReadOnlyList<PerformanceEvidenceGap> UnknownsRequiringTelemetry);

public sealed record PerformanceOpportunity(
    string Tag,
    string Classification,
    string Reason,
    string RequiredValidation,
    string AcceptanceCriteria,
    string Risk,
    string Confidence);

public sealed record PerformanceEvidenceGap(
    string MissingEvidence,
    string WhyRequired,
    string CollectionMethod,
    string Priority,
    string Category);

public sealed record FabricReadiness(
    string CurrentState,
    IReadOnlyList<string> Risks,
    string ReadinessScore,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record PerformanceGate(
    string Gate,
    string Status,
    bool Passed,
    IReadOnlyList<string> MissingArtifacts,
    IReadOnlyList<string> NextActions);
