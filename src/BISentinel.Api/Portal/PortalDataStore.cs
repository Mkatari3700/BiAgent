using System.Data;
using Microsoft.Data.SqlClient;
using BISentinel.Api.Security;

namespace BISentinel.Api.Portal;

public interface IPortalDataStore
{
    Task<PortalDashboardSummary> GetDashboardSummaryAsync(
        Guid userId,
        bool isAdministrator,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken);

    Task<Guid> CreateSupportRequestAsync(
        Guid userId,
        CreateSupportRequest request,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SupportRequestRecord>> GetSupportRequestsAsync(
        Guid userId,
        bool isAdministrator,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken);

    Task<SupportRequestRecord?> RespondToSupportRequestAsync(
        Guid supportRequestId,
        Guid responderUserId,
        RespondToSupportRequest response,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken);
}

public sealed class SqlPortalDataStore(string connectionString) : IPortalDataStore
{
    public async Task<PortalDashboardSummary> GetDashboardSummaryAsync(
        Guid userId,
        bool isAdministrator,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*)
                 FROM dbo.BiAgentReviewRuns AS run
                 WHERE run.ExpiresUtc > SYSUTCDATETIME()
                   AND (
                     @ScopeType IS NULL
                     OR (
                       @ScopeType = N'Organization'
                       AND (
                         run.OrganizationId = @ScopeId
                         OR run.ProjectScopeId IN (
                           SELECT ProjectId FROM dbo.BiAgentProjects WHERE OrganizationId = @ScopeId
                         )
                         OR run.TeamScopeId IN (
                           SELECT team.TeamId
                           FROM dbo.BiAgentTeams AS team
                           INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                           WHERE project.OrganizationId = @ScopeId
                         )
                       )
                     )
                     OR (
                       @ScopeType = N'Project'
                       AND (
                         run.ProjectScopeId = @ScopeId
                         OR run.TeamScopeId IN (
                           SELECT TeamId FROM dbo.BiAgentTeams WHERE ProjectId = @ScopeId
                         )
                       )
                     )
                     OR (@ScopeType = N'Team' AND run.TeamScopeId = @ScopeId)
                   )
                   AND (@IsAdministrator = 1 OR UploadedByUserId = @UserId)),
                (SELECT CASE WHEN @IsAdministrator = 1 THEN COUNT(*) ELSE 0 END
                 FROM dbo.BiAgentGovernanceAuditRuns AS run
                 WHERE (
                   @ScopeType IS NULL
                   OR (
                     @ScopeType = N'Organization'
                     AND (
                       run.OrganizationId = @ScopeId
                       OR run.ProjectScopeId IN (
                         SELECT ProjectId FROM dbo.BiAgentProjects WHERE OrganizationId = @ScopeId
                       )
                       OR run.TeamScopeId IN (
                         SELECT team.TeamId
                         FROM dbo.BiAgentTeams AS team
                         INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                         WHERE project.OrganizationId = @ScopeId
                       )
                     )
                   )
                   OR (
                     @ScopeType = N'Project'
                     AND (
                       run.ProjectScopeId = @ScopeId
                       OR run.TeamScopeId IN (
                         SELECT TeamId FROM dbo.BiAgentTeams WHERE ProjectId = @ScopeId
                       )
                     )
                   )
                   OR (@ScopeType = N'Team' AND run.TeamScopeId = @ScopeId)
                 )),
                (SELECT COUNT(*)
                 FROM dbo.BiAgentSupportRequests AS request
                 WHERE request.Status <> N'Resolved'
                   AND (
                     @ScopeType IS NULL
                     OR (
                       @ScopeType = N'Organization'
                       AND (
                         request.OrganizationId = @ScopeId
                         OR request.ProjectScopeId IN (
                           SELECT ProjectId FROM dbo.BiAgentProjects WHERE OrganizationId = @ScopeId
                         )
                         OR request.TeamScopeId IN (
                           SELECT team.TeamId
                           FROM dbo.BiAgentTeams AS team
                           INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                           WHERE project.OrganizationId = @ScopeId
                         )
                       )
                     )
                     OR (
                       @ScopeType = N'Project'
                       AND (
                         request.ProjectScopeId = @ScopeId
                         OR request.TeamScopeId IN (
                           SELECT TeamId FROM dbo.BiAgentTeams WHERE ProjectId = @ScopeId
                         )
                       )
                     )
                     OR (@ScopeType = N'Team' AND request.TeamScopeId = @ScopeId)
                   )
                   AND (@IsAdministrator = 1 OR UserId = @UserId)),
                (SELECT CASE WHEN @IsAdministrator = 1
                             THEN (SELECT COUNT(DISTINCT userScope.UserId)
                                   FROM dbo.BiAgentApplicationUsers AS scopedUser
                                   INNER JOIN dbo.BiAgentUserScopeAssignments AS userScope
                                       ON userScope.UserId = scopedUser.UserId
                                   LEFT JOIN dbo.BiAgentProjects AS assignedProject
                                       ON assignedProject.ProjectId = userScope.ProjectId
                                   LEFT JOIN dbo.BiAgentTeams AS assignedTeam
                                       ON assignedTeam.TeamId = userScope.TeamId
                                   LEFT JOIN dbo.BiAgentProjects AS assignedTeamProject
                                       ON assignedTeamProject.ProjectId = assignedTeam.ProjectId
                                   WHERE scopedUser.IsEnabled = 1
                                     AND (@ScopeType IS NULL
                                       OR (@ScopeType = N'Organization' AND
                                          (userScope.OrganizationId = @ScopeId
                                           OR assignedProject.OrganizationId = @ScopeId
                                           OR assignedTeamProject.OrganizationId = @ScopeId))
                                       OR (@ScopeType = N'Project' AND
                                          (userScope.ProjectId = @ScopeId
                                           OR assignedTeam.ProjectId = @ScopeId
                                           OR userScope.OrganizationId =
                                              (SELECT OrganizationId FROM dbo.BiAgentProjects WHERE ProjectId = @ScopeId)))
                                       OR (@ScopeType = N'Team' AND
                                          (userScope.TeamId = @ScopeId
                                           OR userScope.ProjectId =
                                              (SELECT ProjectId FROM dbo.BiAgentTeams WHERE TeamId = @ScopeId)
                                           OR userScope.OrganizationId =
                                              (SELECT project.OrganizationId FROM dbo.BiAgentTeams AS team
                                               INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                                               WHERE team.TeamId = @ScopeId)))))
                             ELSE 0 END);
            """;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@IsAdministrator", SqlDbType.Bit).Value = isAdministrator;
        AddScopeParameters(command, scope);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The portal dashboard query returned no summary row.");
        }

        return new PortalDashboardSummary(
            isAdministrator,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }

    public async Task<Guid> CreateSupportRequestAsync(
        Guid userId,
        CreateSupportRequest request,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken)
    {
        var supportRequestId = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.BiAgentSupportRequests
                (SupportRequestId, UserId, Category, Subject, Message,
                 OrganizationId, ProjectScopeId, TeamScopeId)
            VALUES
                (@SupportRequestId, @UserId, @Category, @Subject, @Message,
                 @OrganizationId, @ProjectScopeId, @TeamScopeId);
            """;
        command.Parameters.Add("@SupportRequestId", SqlDbType.UniqueIdentifier).Value = supportRequestId;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@Category", SqlDbType.NVarChar, 40).Value = request.Category;
        command.Parameters.Add("@Subject", SqlDbType.NVarChar, 200).Value = request.Subject;
        command.Parameters.Add("@Message", SqlDbType.NVarChar, 4000).Value = request.Message;
        AddScopeParameters(command, scope);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return supportRequestId;
    }

    public async Task<IReadOnlyList<SupportRequestRecord>> GetSupportRequestsAsync(
        Guid userId,
        bool isAdministrator,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SupportRequestSelect + "\n" + """
            WHERE (@IsAdministrator = 1 OR request.UserId = @UserId)
              AND (
                @ScopeType IS NULL
                OR (
                  @ScopeType = N'Organization'
                  AND (
                    request.OrganizationId = @ScopeId
                    OR request.ProjectScopeId IN (
                      SELECT ProjectId FROM dbo.BiAgentProjects WHERE OrganizationId = @ScopeId
                    )
                    OR request.TeamScopeId IN (
                      SELECT team.TeamId
                      FROM dbo.BiAgentTeams AS team
                      INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                      WHERE project.OrganizationId = @ScopeId
                    )
                  )
                )
                OR (
                  @ScopeType = N'Project'
                  AND (
                    request.ProjectScopeId = @ScopeId
                    OR request.TeamScopeId IN (
                      SELECT TeamId FROM dbo.BiAgentTeams WHERE ProjectId = @ScopeId
                    )
                  )
                )
                OR (@ScopeType = N'Team' AND request.TeamScopeId = @ScopeId)
              )
            ORDER BY
                CASE WHEN @IsAdministrator = 1 AND request.Status = N'Resolved' THEN 1 ELSE 0 END,
                request.UpdatedUtc DESC;
            """;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@IsAdministrator", SqlDbType.Bit).Value = isAdministrator;
        AddScopeParameters(command, scope);
        return await ReadSupportRequestsAsync(command, cancellationToken);
    }

    public async Task<SupportRequestRecord?> RespondToSupportRequestAsync(
        Guid supportRequestId,
        Guid responderUserId,
        RespondToSupportRequest response,
        ActiveWorkspaceScope? scope,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SET NOCOUNT ON;
            UPDATE dbo.BiAgentSupportRequests
            SET AdminResponse = @Response,
                Status = @Status,
                RespondedByUserId = @ResponderUserId,
                RespondedUtc = SYSUTCDATETIME(),
                UpdatedUtc = SYSUTCDATETIME()
            WHERE SupportRequestId = @SupportRequestId
              AND (
                @ScopeType IS NULL
                OR (
                  @ScopeType = N'Organization'
                  AND (
                    OrganizationId = @ScopeId
                    OR ProjectScopeId IN (
                      SELECT ProjectId FROM dbo.BiAgentProjects WHERE OrganizationId = @ScopeId
                    )
                    OR TeamScopeId IN (
                      SELECT team.TeamId
                      FROM dbo.BiAgentTeams AS team
                      INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                      WHERE project.OrganizationId = @ScopeId
                    )
                  )
                )
                OR (
                  @ScopeType = N'Project'
                  AND (
                    ProjectScopeId = @ScopeId
                    OR TeamScopeId IN (
                      SELECT TeamId FROM dbo.BiAgentTeams WHERE ProjectId = @ScopeId
                    )
                  )
                )
                OR (@ScopeType = N'Team' AND TeamScopeId = @ScopeId)
              );
            """ + "\n" + SupportRequestSelect + "\n" + """
            WHERE request.SupportRequestId = @SupportRequestId
              AND (
                @ScopeType IS NULL
                OR (
                  @ScopeType = N'Organization'
                  AND (
                    request.OrganizationId = @ScopeId
                    OR request.ProjectScopeId IN (
                      SELECT ProjectId FROM dbo.BiAgentProjects WHERE OrganizationId = @ScopeId
                    )
                    OR request.TeamScopeId IN (
                      SELECT team.TeamId
                      FROM dbo.BiAgentTeams AS team
                      INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                      WHERE project.OrganizationId = @ScopeId
                    )
                  )
                )
                OR (
                  @ScopeType = N'Project'
                  AND (
                    request.ProjectScopeId = @ScopeId
                    OR request.TeamScopeId IN (
                      SELECT TeamId FROM dbo.BiAgentTeams WHERE ProjectId = @ScopeId
                    )
                  )
                )
                OR (@ScopeType = N'Team' AND request.TeamScopeId = @ScopeId)
              );
            """;
        command.Parameters.Add("@SupportRequestId", SqlDbType.UniqueIdentifier).Value = supportRequestId;
        command.Parameters.Add("@ResponderUserId", SqlDbType.UniqueIdentifier).Value = responderUserId;
        command.Parameters.Add("@Response", SqlDbType.NVarChar, 4000).Value = response.Response;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 20).Value = response.Status;
        AddScopeParameters(command, scope);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadSupportRequest(reader)
            : null;
    }

    private const string SupportRequestSelect = """
        SELECT TOP (200) request.SupportRequestId, request.Category, request.Subject, request.Message,
               request.Status, request.CreatedUtc, request.UpdatedUtc, request.AdminResponse,
               request.RespondedUtc, requester.DisplayName, requester.EmailAddress, responder.DisplayName
        FROM dbo.BiAgentSupportRequests AS request
        INNER JOIN dbo.BiAgentApplicationUsers AS requester ON requester.UserId = request.UserId
        LEFT JOIN dbo.BiAgentApplicationUsers AS responder ON responder.UserId = request.RespondedByUserId
        """;

    private static void AddScopeParameters(SqlCommand command, ActiveWorkspaceScope? scope)
    {
        command.Parameters.Add("@ScopeType", SqlDbType.NVarChar, 20).Value =
            scope is null ? DBNull.Value : scope.ScopeType;
        command.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value =
            scope is null ? DBNull.Value : scope.ScopeId;
        command.Parameters.Add("@OrganizationId", SqlDbType.UniqueIdentifier).Value =
            scope?.ScopeType == "Organization" ? scope.ScopeId : DBNull.Value;
        command.Parameters.Add("@ProjectScopeId", SqlDbType.UniqueIdentifier).Value =
            scope?.ScopeType == "Project" ? scope.ScopeId : DBNull.Value;
        command.Parameters.Add("@TeamScopeId", SqlDbType.UniqueIdentifier).Value =
            scope?.ScopeType == "Team" ? scope.ScopeId : DBNull.Value;
    }

    private static async Task<IReadOnlyList<SupportRequestRecord>> ReadSupportRequestsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var requests = new List<SupportRequestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(ReadSupportRequest(reader));
        }

        return requests;
    }

    private static SupportRequestRecord ReadSupportRequest(SqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetDateTimeOffset(5),
            reader.GetDateTimeOffset(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
}
