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
    string? WebUrl);

public sealed record GovernanceFinding(
    string Code,
    string Severity,
    Guid WorkspaceId,
    string WorkspaceName,
    Guid? ArtifactId,
    string Title,
    string Detail);

public sealed record GovernanceWorkspaceAudit(
    GovernanceWorkspace Workspace,
    IReadOnlyList<GovernanceWorkspaceUser> Users,
    IReadOnlyList<GovernanceArtifact> Reports,
    IReadOnlyList<GovernanceArtifact> SemanticModels);

public sealed record GovernanceAuditResult(
    Guid RunId,
    DateTimeOffset ScannedUtc,
    IReadOnlyList<GovernanceWorkspaceAudit> Workspaces,
    IReadOnlyList<GovernanceFinding> Findings);

public sealed record GovernanceAuditRunSummary(
    Guid RunId,
    DateTimeOffset ScannedUtc,
    string ScannedBy,
    int WorkspaceCount,
    int ReportCount,
    int SemanticModelCount,
    int FindingCount);
