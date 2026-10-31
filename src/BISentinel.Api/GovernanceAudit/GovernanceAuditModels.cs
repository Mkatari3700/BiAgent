using System.Text.Json;

namespace BISentinel.Api.GovernanceAudit;

public sealed record GovernanceAuditConfiguration(
    int Version,
    Guid[] WorkspaceIds,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy);

public sealed record UpdateGovernanceAuditConfigurationRequest(
    int ExpectedVersion,
    Guid[] WorkspaceIds);

public sealed record GovernanceWorkspace(
    Guid Id,
    string Name,
    bool IsReadOnly,
    bool IsOnDedicatedCapacity);

public sealed record GovernanceWorkspaceUser(
    string Identifier,
    string? DisplayName,
    string? EmailAddress,
    string PrincipalType,
    string AccessRight);

public sealed record GovernanceArtifact(
    Guid Id,
    string Name,
    string Type,
    string? DatasetId,
    string? ConfiguredBy,
    string? WebUrl,
    string? Status = null);

public sealed record GovernanceReportConnection(
    Guid ReportId,
    string ReportName,
    string? SemanticModelName,
    string? SourceType,
    string? ConnectionDetails,
    Guid? GatewayId,
    string Status,
    string StatusDetail,
    string GatewayStatus = "Unknown",
    string GatewayStatusDetail = "Gateway status was not returned.");

public sealed record GovernanceWorkspaceArtifactCatalog(
    Guid WorkspaceId,
    string WorkspaceName,
    IReadOnlyList<GovernanceArtifact> Reports,
    IReadOnlyList<GovernanceArtifact> Dashboards);

public sealed record GovernanceFinding(
    string Code,
    string Severity,
    Guid WorkspaceId,
    string WorkspaceName,
    Guid? ArtifactId,
    string? ArtifactType,
    string? ArtifactName,
    string Title,
    string Detail,
    string CheckVersion,
    string CheckName,
    string RuleOwner,
    string EvidenceSource,
    DateTimeOffset EvidenceTimestamp,
    string Status,
    string EvaluationLogic,
    string ExceptionRules,
    string? ExpectedValue,
    string? ActualValue,
    JsonElement Evidence)
{
    public string CheckId => Code;

    public string? OriginalStatus { get; init; }

    public GovernanceExceptionRecord? ExceptionRecord { get; init; }
}

public sealed record CreateGovernanceExceptionRequest(
    Guid RunId,
    Guid WorkspaceId,
    string CheckId,
    Guid? ArtifactId,
    string? ArtifactType,
    string ArtifactName,
    string Owner,
    string Reason,
    DateTimeOffset ExpiresUtc);

public sealed record RevokeGovernanceExceptionRequest(string Reason);

public sealed record GovernanceExceptionAuditEvent(
    string EventType,
    DateTimeOffset EventUtc,
    string Actor,
    string Notes);

public sealed record GovernanceExceptionRecord(
    Guid ExceptionId,
    Guid WorkspaceId,
    string CheckId,
    Guid? ArtifactId,
    string? ArtifactType,
    string ArtifactName,
    string Owner,
    string Reason,
    string ApprovedBy,
    DateTimeOffset ApprovedUtc,
    DateTimeOffset ExpiresUtc,
    string Status,
    Guid SourceRunId,
    IReadOnlyList<GovernanceExceptionAuditEvent> AuditTrail);

public sealed record GovernanceWorkspaceAudit(
    GovernanceWorkspace Workspace,
    IReadOnlyList<GovernanceWorkspaceUser> Users,
    IReadOnlyList<GovernanceArtifact> Reports,
    IReadOnlyList<GovernanceArtifact> SemanticModels,
    IReadOnlyList<GovernanceArtifact> Dashboards)
{
    public IReadOnlyList<GovernanceReportConnection> ReportConnections { get; init; } = [];
}

public sealed record GovernanceAuditResult(
    Guid RunId,
    DateTimeOffset ScannedUtc,
    IReadOnlyList<GovernanceWorkspaceAudit> Workspaces,
    IReadOnlyList<GovernanceFinding> Findings)
{
    public string? ScannedBy { get; init; }

    public IReadOnlyList<Guid> SelectedWorkspaceIds { get; init; } = [];

    public IReadOnlyList<string> SelectedArtifactTypes { get; init; } = [];

    public IReadOnlyList<Guid> SelectedReportIds { get; init; } = [];

    public IReadOnlyList<Guid> SelectedDashboardIds { get; init; } = [];

    public IReadOnlyList<GovernanceArtifact> Apps { get; init; } = [];

    public IReadOnlyList<GovernanceArtifact> Gateways { get; init; } = [];
}

public sealed record GovernanceAuditRunSummary(
    Guid RunId,
    DateTimeOffset ScannedUtc,
    string ScannedBy,
    int WorkspaceCount,
    int ReportCount,
    int SemanticModelCount,
    int DashboardCount,
    int AppCount,
    int GatewayCount,
    int FindingCount);

public sealed record GovernanceApprovedWorkspaceScope(
    int Version,
    IReadOnlyList<Guid> WorkspaceIds,
    DateTimeOffset UpdatedUtc);
