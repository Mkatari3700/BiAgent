namespace BISentinel.Api.GovernanceAudit;

public interface IGovernanceExceptionStore
{
    Task<GovernanceExceptionRecord> ApproveAsync(
        CreateGovernanceExceptionRequest request,
        string approvedBy,
        CancellationToken cancellationToken);

    Task<GovernanceExceptionRecord?> RevokeAsync(
        Guid exceptionId,
        string reason,
        string revokedBy,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GovernanceExceptionRecord>> GetForFindingsAsync(
        IReadOnlyCollection<GovernanceFinding> findings,
        CancellationToken cancellationToken);
}
