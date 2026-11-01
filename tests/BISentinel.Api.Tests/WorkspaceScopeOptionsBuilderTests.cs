using BISentinel.Api.Security;

namespace BISentinel.Api.Tests;

public sealed class WorkspaceScopeOptionsBuilderTests
{
    [Fact]
    public void Build_ReturnsOnlyDirectlyAssignedScopesForRegularUsers()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var firstTeamId = Guid.NewGuid();
        var secondTeamId = Guid.NewGuid();
        var unassignedTeamId = Guid.NewGuid();
        var snapshot = new OrganizationHierarchySnapshot(
            [new OrganizationSummary(organizationId, "Organization")],
            [new ProjectSummary(projectId, organizationId, "Project")],
            [
                new TeamSummary(firstTeamId, projectId, "Team One"),
                new TeamSummary(secondTeamId, projectId, "Team Two"),
                new TeamSummary(unassignedTeamId, projectId, "Other Team")
            ],
            [],
            [
                new UserScopeAssignmentSummary(Guid.NewGuid(), userId, "Team", firstTeamId, "ReadOnly"),
                new UserScopeAssignmentSummary(Guid.NewGuid(), userId, "Team", secondTeamId, "ReadOnly"),
                new UserScopeAssignmentSummary(Guid.NewGuid(), otherUserId, "Team", unassignedTeamId, "ReadOnly")
            ]);

        var options = WorkspaceScopeOptionsBuilder.Build(snapshot, userId, isRootAdmin: false);

        Assert.Equal(2, options.Count);
        Assert.All(options, option => Assert.Equal("Team", option.ScopeType));
        Assert.Contains(options, option => option.ScopeId == firstTeamId);
        Assert.Contains(options, option => option.ScopeId == secondTeamId);
        Assert.DoesNotContain(options, option => option.ScopeId == unassignedTeamId);
        Assert.DoesNotContain(options, option => option.ScopeId == organizationId);
        Assert.DoesNotContain(options, option => option.ScopeId == projectId);
    }

    [Fact]
    public void Build_ReturnsAllScopesForRootAdministrators()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var snapshot = new OrganizationHierarchySnapshot(
            [new OrganizationSummary(organizationId, "Organization")],
            [new ProjectSummary(projectId, organizationId, "Project")],
            [new TeamSummary(teamId, projectId, "Team")],
            [],
            []);

        var options = WorkspaceScopeOptionsBuilder.Build(snapshot, userId, isRootAdmin: true);

        Assert.Equal(3, options.Count);
    }
}
