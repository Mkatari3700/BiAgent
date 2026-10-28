namespace BISentinel.Api.GovernanceAudit;

public interface IGovernanceAuditConfigurationStore
{
    Task<GovernanceAuditConfiguration> GetAsync(CancellationToken cancellationToken);

    Task<GovernanceAuditConfiguration?> UpdateAsync(
        Guid[] workspaceIds,
        int expectedVersion,
        string updatedBy,
        CancellationToken cancellationToken);
}

public interface IGovernanceAuditHistoryStore
{
    Task<Guid> SaveAsync(GovernanceAuditResult result, string scannedBy, CancellationToken cancellationToken);

    Task<IReadOnlyList<GovernanceAuditRunSummary>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<GovernanceAuditResult?> GetAsync(Guid runId, CancellationToken cancellationToken);
}
