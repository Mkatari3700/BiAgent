namespace BISentinel.Api.Security;

public sealed record WorkspaceScopeOption(
    string ScopeType,
    Guid ScopeId,
    string Label);

public sealed record WorkspaceScopesResponse(
    IReadOnlyList<WorkspaceScopeOption> Scopes,
    bool IsRootAdmin);

public sealed record WorkspaceMemberSummary(
    Guid UserId,
    string DisplayName,
    string? EmailAddress,
    IReadOnlyList<string> Roles);

public sealed record WorkspaceContextResponse(
    ActiveWorkspaceScope? ActiveScope,
    string? ActiveScopeLabel,
    IReadOnlyList<WorkspaceMemberSummary> Members);

public static class WorkspaceScopeOptionsBuilder
{
    public static IReadOnlyList<WorkspaceScopeOption> Build(
        OrganizationHierarchySnapshot snapshot,
        Guid userId,
        bool isRootAdmin)
    {
        var organizationsById = snapshot.Organizations.ToDictionary(
            organization => organization.OrganizationId,
            organization => organization.Name);
        var options = new List<WorkspaceScopeOption>();
        options.AddRange(snapshot.Organizations.Select(organization =>
            new WorkspaceScopeOption("Organization", organization.OrganizationId, organization.Name)));
        options.AddRange(snapshot.Projects.Select(project =>
            new WorkspaceScopeOption(
                "Project",
                project.ProjectId,
                $"{organizationsById.GetValueOrDefault(project.OrganizationId, "Organization")} / {project.Name}")));
        options.AddRange(snapshot.Teams.Select(team =>
        {
            var project = snapshot.Projects.FirstOrDefault(item => item.ProjectId == team.ProjectId);
            var organization = project is null ? null : organizationsById.GetValueOrDefault(project.OrganizationId);
            return new WorkspaceScopeOption(
                "Team",
                team.TeamId,
                $"{organization ?? "Organization"} / {project?.Name ?? "Project"} / {team.Name}");
        }));

        if (isRootAdmin)
        {
            return options;
        }

        var directlyAssignedScopes = snapshot.Assignments
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => (assignment.ScopeType, assignment.ScopeId))
            .ToHashSet();
        return options
            .Where(option => directlyAssignedScopes.Contains((option.ScopeType, option.ScopeId)))
            .ToArray();
    }
}
