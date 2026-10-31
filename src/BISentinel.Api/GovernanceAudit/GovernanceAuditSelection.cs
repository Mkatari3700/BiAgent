namespace BISentinel.Api.GovernanceAudit;

public static class GovernanceAuditSelection
{
    public static Guid[] ValidateWorkspaceIds(
        IReadOnlyCollection<Guid> approvedWorkspaceIds,
        IReadOnlyCollection<Guid>? requestedWorkspaceIds,
        int maximumWorkspaces)
    {
        var selectedWorkspaceIds = requestedWorkspaceIds?.ToArray() ?? approvedWorkspaceIds.ToArray();
        if (selectedWorkspaceIds.Length == 0 ||
            selectedWorkspaceIds.Length > maximumWorkspaces ||
            selectedWorkspaceIds.Any(id => id == Guid.Empty) ||
            selectedWorkspaceIds.Distinct().Count() != selectedWorkspaceIds.Length ||
            selectedWorkspaceIds.Except(approvedWorkspaceIds).Any())
        {
            throw new ArgumentException(
                "Choose one or more distinct workspaces from the administrator-approved scope.",
                nameof(requestedWorkspaceIds));
        }

        return selectedWorkspaceIds;
    }
}
