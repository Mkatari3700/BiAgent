using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public sealed record OrganizationHierarchySnapshot(
    IReadOnlyList<OrganizationSummary> Organizations,
    IReadOnlyList<ProjectSummary> Projects,
    IReadOnlyList<TeamSummary> Teams,
    IReadOnlyList<ApplicationUserSummary> Users,
    IReadOnlyList<UserScopeAssignmentSummary> Assignments);

public sealed record OrganizationSummary(Guid OrganizationId, string Name);
public sealed record ProjectSummary(Guid ProjectId, Guid OrganizationId, string Name);
public sealed record TeamSummary(Guid TeamId, Guid ProjectId, string Name);
public sealed record UserScopeAssignmentSummary(
    Guid AssignmentId,
    Guid UserId,
    string ScopeType,
    Guid ScopeId,
    string RoleName);

public sealed record CreateOrganizationRequest(string Name);
public sealed record CreateProjectRequest(Guid OrganizationId, string Name);
public sealed record CreateTeamRequest(Guid ProjectId, string Name);
public sealed record SetUserScopeAssignmentRequest(
    Guid UserId,
    string ScopeType,
    Guid ScopeId,
    string RoleName,
    bool IsAssigned);

public enum HierarchyMutationResult
{
    Updated,
    Forbidden,
    NotFound,
    Conflict
}

public interface IOrganizationHierarchyStore
{
    Task<bool> IsScopeAccessibleAsync(
        Guid tenantId,
        Guid userId,
        string scopeType,
        Guid scopeId,
        bool isRootAdmin,
        CancellationToken cancellationToken);

    Task<OrganizationHierarchySnapshot> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        Guid bootstrapAdminObjectId,
        bool isRootAdmin,
        CancellationToken cancellationToken);

    Task<(HierarchyMutationResult Result, Guid Id)> CreateOrganizationAsync(
        Guid tenantId,
        Guid actorUserId,
        string name,
        bool isRootAdmin,
        CancellationToken cancellationToken);

    Task<(HierarchyMutationResult Result, Guid Id)> CreateProjectAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid organizationId,
        string name,
        bool isRootAdmin,
        CancellationToken cancellationToken);

    Task<(HierarchyMutationResult Result, Guid Id)> CreateTeamAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid projectId,
        string name,
        bool isRootAdmin,
        CancellationToken cancellationToken);

    Task<HierarchyMutationResult> SetAssignmentAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        string scopeType,
        Guid scopeId,
        string roleName,
        bool isAssigned,
        bool isRootAdmin,
        CancellationToken cancellationToken);
}

public sealed class SqlOrganizationHierarchyStore(string connectionString) : IOrganizationHierarchyStore
{
    public async Task<bool> IsScopeAccessibleAsync(
        Guid tenantId,
        Guid userId,
        string scopeType,
        Guid scopeId,
        bool isRootAdmin,
        CancellationToken cancellationToken)
    {
        if (scopeType is not ("Organization" or "Project" or "Team"))
        {
            return false;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = scopeType switch
        {
            "Organization" => """
                SELECT CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentApplicationUsers AS actor
                    WHERE actor.UserId = @UserId AND actor.TenantId = @TenantId
                      AND EXISTS
                      (
                          SELECT 1 FROM dbo.BiAgentOrganizations AS organization
                          WHERE organization.OrganizationId = @ScopeId AND organization.IsEnabled = 1
                            AND (@IsRootAdmin = 1 OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.BiAgentUserScopeAssignments AS assignment
                                WHERE assignment.UserId = @UserId
                                  AND assignment.ScopeType = N'Organization'
                                  AND assignment.OrganizationId = organization.OrganizationId
                            ))
                      )
                ) THEN 1 ELSE 0 END);
                """,
            "Project" => """
                SELECT CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentApplicationUsers AS actor
                    WHERE actor.UserId = @UserId AND actor.TenantId = @TenantId
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.BiAgentProjects AS project
                          INNER JOIN dbo.BiAgentOrganizations AS organization
                              ON organization.OrganizationId = project.OrganizationId
                          WHERE project.ProjectId = @ScopeId
                            AND project.IsEnabled = 1 AND organization.IsEnabled = 1
                            AND (@IsRootAdmin = 1 OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.BiAgentUserScopeAssignments AS assignment
                                WHERE assignment.UserId = @UserId
                                  AND assignment.ScopeType = N'Project'
                                  AND assignment.ProjectId = project.ProjectId
                            ))
                      )
                ) THEN 1 ELSE 0 END);
                """,
            _ => """
                SELECT CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentApplicationUsers AS actor
                    WHERE actor.UserId = @UserId AND actor.TenantId = @TenantId
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.BiAgentTeams AS team
                          INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                          INNER JOIN dbo.BiAgentOrganizations AS organization
                              ON organization.OrganizationId = project.OrganizationId
                          WHERE team.TeamId = @ScopeId AND team.IsEnabled = 1
                            AND project.IsEnabled = 1 AND organization.IsEnabled = 1
                            AND (@IsRootAdmin = 1 OR EXISTS
                            (
                                SELECT 1
                                FROM dbo.BiAgentUserScopeAssignments AS assignment
                                WHERE assignment.UserId = @UserId
                                  AND assignment.ScopeType = N'Team'
                                  AND assignment.TeamId = team.TeamId
                            ))
                      )
                ) THEN 1 ELSE 0 END);
                """
        };
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value = scopeId;
        command.Parameters.Add("@IsRootAdmin", SqlDbType.Bit).Value = isRootAdmin;
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<OrganizationHierarchySnapshot> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        Guid bootstrapAdminObjectId,
        bool isRootAdmin,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var organizations = new List<OrganizationSummary>();
        await using (var command = new SqlCommand(
            """
            SELECT DISTINCT organization.OrganizationId, organization.Name
            FROM dbo.BiAgentOrganizations AS organization
            LEFT JOIN dbo.BiAgentProjects AS project ON project.OrganizationId = organization.OrganizationId
            LEFT JOIN dbo.BiAgentTeams AS team ON team.ProjectId = project.ProjectId
            WHERE organization.IsEnabled = 1
              AND (@IsRootAdmin = 1 OR EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentUserScopeAssignments AS assignment
                  WHERE assignment.UserId = @UserId
                    AND (assignment.OrganizationId = organization.OrganizationId
                      OR assignment.ProjectId = project.ProjectId
                      OR assignment.TeamId = team.TeamId)
              ))
            ORDER BY organization.Name;
            """,
            connection))
        {
            AddScopeParameters(command, userId, isRootAdmin);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                organizations.Add(new OrganizationSummary(reader.GetGuid(0), reader.GetString(1)));
            }
        }

        var projects = new List<ProjectSummary>();
        await using (var command = new SqlCommand(
            """
            SELECT project.ProjectId, project.OrganizationId, project.Name
            FROM dbo.BiAgentProjects AS project
            WHERE project.IsEnabled = 1
              AND (@IsRootAdmin = 1 OR EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentUserScopeAssignments AS assignment
                  LEFT JOIN dbo.BiAgentTeams AS team ON team.TeamId = assignment.TeamId
                  WHERE assignment.UserId = @UserId
                    AND (assignment.OrganizationId = project.OrganizationId
                      OR assignment.ProjectId = project.ProjectId
                      OR team.ProjectId = project.ProjectId)
              ))
            ORDER BY project.Name;
            """,
            connection))
        {
            AddScopeParameters(command, userId, isRootAdmin);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                projects.Add(new ProjectSummary(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
            }
        }

        var teams = new List<TeamSummary>();
        await using (var command = new SqlCommand(
            """
            SELECT team.TeamId, team.ProjectId, team.Name
            FROM dbo.BiAgentTeams AS team
            INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
            WHERE team.IsEnabled = 1 AND project.IsEnabled = 1
              AND (@IsRootAdmin = 1 OR EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentUserScopeAssignments AS assignment
                  WHERE assignment.UserId = @UserId
                    AND (assignment.OrganizationId = project.OrganizationId
                      OR assignment.ProjectId = project.ProjectId
                      OR assignment.TeamId = team.TeamId)
              ))
            ORDER BY team.Name;
            """,
            connection))
        {
            AddScopeParameters(command, userId, isRootAdmin);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                teams.Add(new TeamSummary(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
            }
        }

        var users = new List<ApplicationUserSummary>();
        await using (var command = new SqlCommand(
            """
            SELECT u.UserId, u.DisplayName, u.EmailAddress, u.FirstSeenUtc, u.LastSeenUtc,
                   CONVERT(bit, CASE WHEN adminRole.UserId IS NULL THEN 0 ELSE 1 END),
                   CONVERT(bit, CASE WHEN managerRole.UserId IS NULL THEN 0 ELSE 1 END),
                   CONVERT(bit, CASE WHEN leadRole.UserId IS NULL THEN 0 ELSE 1 END),
                   CONVERT(bit, CASE WHEN u.EntraObjectId = @BootstrapAdminObjectId THEN 1 ELSE 0 END)
            FROM dbo.BiAgentApplicationUsers AS u
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS adminRole
              ON adminRole.UserId = u.UserId AND adminRole.RoleName = N'ApplicationAdmin'
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS managerRole
              ON managerRole.UserId = u.UserId AND managerRole.RoleName = N'ReviewManager'
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS leadRole
              ON leadRole.UserId = u.UserId AND leadRole.RoleName = N'ReviewLead'
            WHERE u.TenantId = @TenantId AND u.IsEnabled = 1
              AND (@IsRootAdmin = 1 OR u.UserId = @UserId OR EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentUserScopeAssignments AS targetAssignment
                  WHERE targetAssignment.UserId = u.UserId
                    AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.BiAgentUserScopeAssignments AS actorAssignment
                        LEFT JOIN dbo.BiAgentProjects AS targetProject
                          ON targetProject.ProjectId = targetAssignment.ProjectId
                        LEFT JOIN dbo.BiAgentTeams AS targetTeam
                          ON targetTeam.TeamId = targetAssignment.TeamId
                        LEFT JOIN dbo.BiAgentProjects AS targetTeamProject
                          ON targetTeamProject.ProjectId = targetTeam.ProjectId
                        WHERE actorAssignment.UserId = @UserId
                          AND (actorAssignment.OrganizationId = COALESCE(
                                  targetAssignment.OrganizationId,
                                  targetProject.OrganizationId,
                                  targetTeamProject.OrganizationId)
                            OR actorAssignment.ProjectId = COALESCE(
                                  targetAssignment.ProjectId,
                                  targetTeam.ProjectId)
                            OR actorAssignment.TeamId = targetAssignment.TeamId)
                    )
              ))
            ORDER BY u.DisplayName;
            """,
            connection))
        {
            AddScopeParameters(command, userId, isRootAdmin);
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            command.Parameters.Add("@BootstrapAdminObjectId", SqlDbType.UniqueIdentifier).Value = bootstrapAdminObjectId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                users.Add(new ApplicationUserSummary(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8),
                    reader.GetDateTimeOffset(3),
                    reader.GetDateTimeOffset(4)));
            }
        }

        var assignments = new List<UserScopeAssignmentSummary>();
        await using (var command = new SqlCommand(
            """
            SELECT assignment.AssignmentId, assignment.UserId, assignment.ScopeType,
                   COALESCE(assignment.OrganizationId, assignment.ProjectId, assignment.TeamId), assignment.RoleName
            FROM dbo.BiAgentUserScopeAssignments AS assignment
            INNER JOIN dbo.BiAgentApplicationUsers AS u ON u.UserId = assignment.UserId
            WHERE u.TenantId = @TenantId
              AND (@IsRootAdmin = 1 OR assignment.UserId = @UserId OR EXISTS
              (
                  SELECT 1
                  FROM dbo.BiAgentUserScopeAssignments AS actorAssignment
                  LEFT JOIN dbo.BiAgentProjects AS targetProject
                    ON targetProject.ProjectId = assignment.ProjectId
                  LEFT JOIN dbo.BiAgentTeams AS targetTeam
                    ON targetTeam.TeamId = assignment.TeamId
                  LEFT JOIN dbo.BiAgentProjects AS targetTeamProject
                    ON targetTeamProject.ProjectId = targetTeam.ProjectId
                  WHERE actorAssignment.UserId = @UserId
                    AND (actorAssignment.OrganizationId = COALESCE(
                            assignment.OrganizationId,
                            targetProject.OrganizationId,
                            targetTeamProject.OrganizationId)
                      OR actorAssignment.ProjectId = COALESCE(
                            assignment.ProjectId,
                            targetTeam.ProjectId)
                      OR actorAssignment.TeamId = assignment.TeamId)
              ))
            ORDER BY assignment.ScopeType, assignment.RoleName;
            """,
            connection))
        {
            AddScopeParameters(command, userId, isRootAdmin);
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                assignments.Add(new UserScopeAssignmentSummary(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetGuid(3),
                    reader.GetString(4)));
            }
        }

        return new OrganizationHierarchySnapshot(organizations, projects, teams, users, assignments);
    }

    public async Task<(HierarchyMutationResult Result, Guid Id)> CreateOrganizationAsync(
        Guid tenantId,
        Guid actorUserId,
        string name,
        bool isRootAdmin,
        CancellationToken cancellationToken)
    {
        if (!isRootAdmin)
        {
            return (HierarchyMutationResult.Forbidden, Guid.Empty);
        }

        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.BiAgentApplicationUsers
                WHERE UserId = @ActorUserId AND TenantId = @TenantId AND IsEnabled = 1
            )
                THROW 51001, 'The acting administrator is not a tenant user.', 1;
            INSERT INTO dbo.BiAgentOrganizations (OrganizationId, Name, CreatedByUserId)
            VALUES (@Id, @Name, @ActorUserId);
            """,
            connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
        command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return (HierarchyMutationResult.Updated, id);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return (HierarchyMutationResult.Conflict, Guid.Empty);
        }
    }

    public async Task<(HierarchyMutationResult Result, Guid Id)> CreateProjectAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid organizationId,
        string name,
        bool isRootAdmin,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.BiAgentOrganizations
                WHERE OrganizationId = @OrganizationId AND IsEnabled = 1
            )
                SELECT 2;
            ELSE IF @IsRootAdmin = 0 AND NOT EXISTS
            (
                SELECT 1
                FROM dbo.BiAgentUserScopeAssignments AS assignment
                INNER JOIN dbo.BiAgentApplicationUsers AS actor
                  ON actor.UserId = assignment.UserId AND actor.TenantId = @TenantId AND actor.IsEnabled = 1
                WHERE assignment.UserId = @ActorUserId
                  AND assignment.OrganizationId = @OrganizationId
                  AND assignment.RoleName = N'OrgLead'
            )
                SELECT 1;
            ELSE
            BEGIN
                INSERT INTO dbo.BiAgentProjects (ProjectId, OrganizationId, Name, CreatedByUserId)
                VALUES (@Id, @OrganizationId, @Name, @ActorUserId);
                SELECT 0;
            END
            """,
            connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@OrganizationId", SqlDbType.UniqueIdentifier).Value = organizationId;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
        command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        command.Parameters.Add("@IsRootAdmin", SqlDbType.Bit).Value = isRootAdmin;
        try
        {
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return (result switch
            {
                0 => HierarchyMutationResult.Updated,
                1 => HierarchyMutationResult.Forbidden,
                _ => HierarchyMutationResult.NotFound
            }, result == 0 ? id : Guid.Empty);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return (HierarchyMutationResult.Conflict, Guid.Empty);
        }
    }

    public async Task<(HierarchyMutationResult Result, Guid Id)> CreateTeamAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid projectId,
        string name,
        bool isRootAdmin,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.BiAgentProjects
                WHERE ProjectId = @ProjectId AND IsEnabled = 1
            )
                SELECT 2;
            ELSE IF @IsRootAdmin = 0 AND NOT EXISTS
            (
                SELECT 1
                FROM dbo.BiAgentProjects AS project
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON assignment.UserId = @ActorUserId
                 AND assignment.RoleName IN (N'OrgLead', N'ProjectLead')
                 AND (assignment.ProjectId = project.ProjectId OR assignment.OrganizationId = project.OrganizationId)
                INNER JOIN dbo.BiAgentApplicationUsers AS actor
                  ON actor.UserId = assignment.UserId AND actor.TenantId = @TenantId AND actor.IsEnabled = 1
                WHERE project.ProjectId = @ProjectId
            )
                SELECT 1;
            ELSE
            BEGIN
                INSERT INTO dbo.BiAgentTeams (TeamId, ProjectId, Name, CreatedByUserId)
                VALUES (@Id, @ProjectId, @Name, @ActorUserId);
                SELECT 0;
            END
            """,
            connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = projectId;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
        command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        command.Parameters.Add("@IsRootAdmin", SqlDbType.Bit).Value = isRootAdmin;
        try
        {
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return (result switch
            {
                0 => HierarchyMutationResult.Updated,
                1 => HierarchyMutationResult.Forbidden,
                _ => HierarchyMutationResult.NotFound
            }, result == 0 ? id : Guid.Empty);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return (HierarchyMutationResult.Conflict, Guid.Empty);
        }
    }

    public async Task<HierarchyMutationResult> SetAssignmentAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        string scopeType,
        Guid scopeId,
        string roleName,
        bool isAssigned,
        bool isRootAdmin,
        CancellationToken cancellationToken)
    {
        var roleIsValid = (scopeType, roleName) is
            ("Organization", "OrgLead" or "ReadOnly") or
            ("Project", "ProjectLead" or "ReadOnly") or
            ("Team", "TeamLead" or "ReadOnly");
        if (!roleIsValid)
        {
            return HierarchyMutationResult.Forbidden;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await using (var command = new SqlCommand(
            """
            SELECT
                CONVERT(bit, CASE WHEN target.UserId IS NULL THEN 0 ELSE 1 END),
                CONVERT(bit, CASE
                    WHEN @ScopeType = N'Organization' AND EXISTS
                    (
                        SELECT 1 FROM dbo.BiAgentOrganizations
                        WHERE OrganizationId = @ScopeId AND IsEnabled = 1
                    ) THEN 1
                    WHEN @ScopeType = N'Project' AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.BiAgentProjects AS project
                        INNER JOIN dbo.BiAgentOrganizations AS organization
                          ON organization.OrganizationId = project.OrganizationId AND organization.IsEnabled = 1
                        WHERE project.ProjectId = @ScopeId AND project.IsEnabled = 1
                    ) THEN 1
                    WHEN @ScopeType = N'Team' AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.BiAgentTeams AS team
                        INNER JOIN dbo.BiAgentProjects AS project
                          ON project.ProjectId = team.ProjectId AND project.IsEnabled = 1
                        INNER JOIN dbo.BiAgentOrganizations AS organization
                          ON organization.OrganizationId = project.OrganizationId AND organization.IsEnabled = 1
                        WHERE team.TeamId = @ScopeId AND team.IsEnabled = 1
                    ) THEN 1
                    ELSE 0 END),
                CONVERT(bit, CASE WHEN @IsRootAdmin = 1 THEN 1
                    WHEN @RoleName = N'OrgLead' THEN 0
                    WHEN @ScopeType = N'Organization' AND EXISTS
                    (
                        SELECT 1 FROM dbo.BiAgentUserScopeAssignments AS actorAssignment
                        WHERE actorAssignment.UserId = @ActorUserId
                          AND actorAssignment.OrganizationId = @ScopeId
                          AND actorAssignment.RoleName = N'OrgLead'
                          AND @RoleName = N'ReadOnly'
                    ) THEN 1
                    WHEN @ScopeType = N'Project' AND EXISTS
                    (
                        SELECT 1 FROM dbo.BiAgentProjects AS project
                        INNER JOIN dbo.BiAgentUserScopeAssignments AS actorAssignment
                          ON actorAssignment.UserId = @ActorUserId
                         AND ((actorAssignment.RoleName = N'OrgLead'
                               AND actorAssignment.OrganizationId = project.OrganizationId
                               AND @RoleName IN (N'ProjectLead', N'ReadOnly'))
                           OR (actorAssignment.RoleName = N'ProjectLead'
                               AND actorAssignment.ProjectId = project.ProjectId
                               AND @RoleName = N'ReadOnly'))
                        WHERE project.ProjectId = @ScopeId
                    ) THEN 1
                    WHEN @ScopeType = N'Team' AND EXISTS
                    (
                        SELECT 1
                        FROM dbo.BiAgentTeams AS team
                        INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                        INNER JOIN dbo.BiAgentUserScopeAssignments AS actorAssignment
                          ON actorAssignment.UserId = @ActorUserId
                         AND ((actorAssignment.RoleName = N'OrgLead'
                               AND actorAssignment.OrganizationId = project.OrganizationId
                               AND @RoleName IN (N'TeamLead', N'ReadOnly'))
                           OR (actorAssignment.RoleName = N'ProjectLead'
                               AND actorAssignment.ProjectId = project.ProjectId
                               AND @RoleName IN (N'TeamLead', N'ReadOnly'))
                           OR (actorAssignment.RoleName = N'TeamLead'
                               AND actorAssignment.TeamId = team.TeamId
                               AND @RoleName = N'ReadOnly'))
                        WHERE team.TeamId = @ScopeId
                    ) THEN 1 ELSE 0 END)
            FROM dbo.BiAgentApplicationUsers AS target
            WHERE target.UserId = @TargetUserId AND target.TenantId = @TenantId AND target.IsEnabled = 1;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            command.Parameters.Add("@IsRootAdmin", SqlDbType.Bit).Value = isRootAdmin;
            command.Parameters.Add("@RoleName", SqlDbType.NVarChar, 20).Value = roleName;
            command.Parameters.Add("@ScopeType", SqlDbType.NVarChar, 20).Value = scopeType;
            command.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value = scopeId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0))
            {
                await transaction.RollbackAsync(cancellationToken);
                return HierarchyMutationResult.NotFound;
            }

            if (!reader.GetBoolean(1))
            {
                await transaction.RollbackAsync(cancellationToken);
                return HierarchyMutationResult.NotFound;
            }

            if (!reader.GetBoolean(2))
            {
                await transaction.RollbackAsync(cancellationToken);
                return HierarchyMutationResult.Forbidden;
            }
        }

        var scopeColumn = scopeType switch
        {
            "Organization" => "OrganizationId",
            "Project" => "ProjectId",
            _ => "TeamId"
        };
        var nullColumn = scopeType switch
        {
            "Organization" => "ProjectId IS NULL AND TeamId IS NULL",
            "Project" => "OrganizationId IS NULL AND TeamId IS NULL",
            _ => "OrganizationId IS NULL AND ProjectId IS NULL"
        };
        var commandText = isAssigned
            ? $"""
              IF NOT EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentUserScopeAssignments WITH (UPDLOCK, HOLDLOCK)
                  WHERE UserId = @TargetUserId AND {scopeColumn} = @ScopeId AND RoleName = @RoleName
              )
              INSERT INTO dbo.BiAgentUserScopeAssignments
                  (AssignmentId, UserId, ScopeType, OrganizationId, ProjectId, TeamId, RoleName, AssignedByUserId)
              VALUES
                  (@AssignmentId, @TargetUserId, @ScopeType,
                   {(scopeType == "Organization" ? "@ScopeId" : "@NullId")},
                   {(scopeType == "Project" ? "@ScopeId" : "@NullId")},
                   {(scopeType == "Team" ? "@ScopeId" : "@NullId")},
                   @RoleName, @ActorUserId);
              """
            : $"""
              DELETE FROM dbo.BiAgentUserScopeAssignments
              WHERE UserId = @TargetUserId AND {scopeColumn} = @ScopeId
                AND RoleName = @RoleName AND {nullColumn};
              """;
        await using var mutation = new SqlCommand(commandText, connection, transaction);
        mutation.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
        mutation.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value = scopeId;
        mutation.Parameters.Add("@NullId", SqlDbType.UniqueIdentifier).Value = DBNull.Value;
        mutation.Parameters.Add("@RoleName", SqlDbType.NVarChar, 20).Value = roleName;
        mutation.Parameters.Add("@ScopeType", SqlDbType.NVarChar, 20).Value = scopeType;
        mutation.Parameters.Add("@AssignmentId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
        mutation.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        await mutation.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return HierarchyMutationResult.Updated;
    }

    private static void AddScopeParameters(SqlCommand command, Guid userId, bool isRootAdmin)
    {
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@IsRootAdmin", SqlDbType.Bit).Value = isRootAdmin;
    }
}
