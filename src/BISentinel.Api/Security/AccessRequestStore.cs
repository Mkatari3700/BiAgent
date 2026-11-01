using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public sealed record AccessRequestScopeOption(string ScopeType, Guid ScopeId, string Name);

public sealed record AccessRequestRecord(
    Guid AccessRequestId,
    string RequesterName,
    string? RequesterEmail,
    string ScopeType,
    Guid ScopeId,
    string ScopeName,
    string RequestedRole,
    string? Message,
    string Status,
    string? ResolutionMessage,
    string? RespondedByName,
    DateTimeOffset SubmittedUtc,
    DateTimeOffset? ResolvedUtc);

public sealed record AccessRequestSettings(int EscalationDays);
public sealed record CreateAccessRequestRequest(
    string ScopeType,
    Guid ScopeId,
    string RoleName,
    string? Message);
public sealed record UpdateAccessRequestSettingsRequest(int EscalationDays);
public sealed record AccessRequestDecision(string Decision, string? Message);
public sealed record AccessRequestEmail(
    Guid NotificationId,
    string TemplateKey,
    string RecipientName,
    string RecipientEmail,
    string RequesterName,
    string? RequesterEmail,
    string ScopeType,
    string ScopeName,
    string RequestedRole,
    string? RequestMessage,
    string? ResolutionMessage,
    DateTimeOffset SubmittedUtc);

public enum AccessRequestMutationResult
{
    Updated,
    Invalid,
    Duplicate,
    NotFound,
    Forbidden,
    AlreadyResolved
}

public interface IAccessRequestStore
{
    Task<IReadOnlyList<AccessRequestScopeOption>> GetScopesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessRequestRecord>> GetMyRequestsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessRequestRecord>> GetInboxAsync(
        Guid tenantId,
        Guid userId,
        bool isRootAdmin,
        int escalationDays,
        CancellationToken cancellationToken);
    Task<AccessRequestMutationResult> CreateAsync(
        Guid tenantId,
        Guid userId,
        string scopeType,
        Guid scopeId,
        string roleName,
        string? message,
        CancellationToken cancellationToken);
    Task<AccessRequestMutationResult> DecideAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid accessRequestId,
        bool isRootAdmin,
        string decision,
        string? message,
        int escalationDays,
        CancellationToken cancellationToken);
    Task<AccessRequestSettings> GetSettingsAsync(CancellationToken cancellationToken);
    Task SetSettingsAsync(Guid actorUserId, int escalationDays, CancellationToken cancellationToken);
    Task<int> QueueDailyNotificationsAsync(int escalationDays, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessRequestEmail>> GetPendingEmailsAsync(
        int batchSize,
        CancellationToken cancellationToken);
    Task RecordEmailAttemptAsync(
        Guid notificationId,
        bool sent,
        string? error,
        CancellationToken cancellationToken);
}

public sealed class SqlAccessRequestStore(string connectionString) : IAccessRequestStore
{
    public async Task<IReadOnlyList<AccessRequestScopeOption>> GetScopesAsync(
        CancellationToken cancellationToken)
    {
        var scopes = new List<AccessRequestScopeOption>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT N'Organization', organization.OrganizationId, organization.Name
            FROM dbo.BiAgentOrganizations AS organization
            WHERE organization.IsEnabled = 1
            UNION ALL
            SELECT N'Project', project.ProjectId, CONCAT(organization.Name, N' / ', project.Name)
            FROM dbo.BiAgentProjects AS project
            INNER JOIN dbo.BiAgentOrganizations AS organization
              ON organization.OrganizationId = project.OrganizationId AND organization.IsEnabled = 1
            WHERE project.IsEnabled = 1
            UNION ALL
            SELECT N'Team', team.TeamId,
                   CONCAT(organization.Name, N' / ', project.Name, N' / ', team.Name)
            FROM dbo.BiAgentTeams AS team
            INNER JOIN dbo.BiAgentProjects AS project
              ON project.ProjectId = team.ProjectId AND project.IsEnabled = 1
            INNER JOIN dbo.BiAgentOrganizations AS organization
              ON organization.OrganizationId = project.OrganizationId AND organization.IsEnabled = 1
            WHERE team.IsEnabled = 1
            ORDER BY 1, 3;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            scopes.Add(new AccessRequestScopeOption(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2)));
        }

        return scopes;
    }

    public async Task<IReadOnlyList<AccessRequestRecord>> GetMyRequestsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT request.AccessRequestId, requester.DisplayName, requester.EmailAddress,
                   request.ScopeType, COALESCE(request.OrganizationId, request.ProjectId, request.TeamId),
                   CASE request.ScopeType
                       WHEN N'Organization' THEN organization.Name
                       WHEN N'Project' THEN CONCAT(parentOrganization.Name, N' / ', project.Name)
                       ELSE CONCAT(teamOrganization.Name, N' / ', parentProject.Name, N' / ', team.Name)
                   END,
                   request.RequestedRole, request.Message, request.Status, request.ResolutionMessage,
                   responder.DisplayName, request.SubmittedUtc, request.ResolvedUtc
            FROM dbo.BiAgentAccessRequests AS request
            INNER JOIN dbo.BiAgentApplicationUsers AS requester ON requester.UserId = request.UserId
            LEFT JOIN dbo.BiAgentOrganizations AS organization ON organization.OrganizationId = request.OrganizationId
            LEFT JOIN dbo.BiAgentProjects AS project ON project.ProjectId = request.ProjectId
            LEFT JOIN dbo.BiAgentOrganizations AS parentOrganization ON parentOrganization.OrganizationId = project.OrganizationId
            LEFT JOIN dbo.BiAgentTeams AS team ON team.TeamId = request.TeamId
            LEFT JOIN dbo.BiAgentProjects AS parentProject ON parentProject.ProjectId = team.ProjectId
            LEFT JOIN dbo.BiAgentOrganizations AS teamOrganization ON teamOrganization.OrganizationId = parentProject.OrganizationId
            LEFT JOIN dbo.BiAgentApplicationUsers AS responder ON responder.UserId = request.RespondedByUserId
            WHERE request.UserId = @UserId
            ORDER BY request.SubmittedUtc DESC;
            """,
            connection);
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        return await ReadRequestsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<AccessRequestRecord>> GetInboxAsync(
        Guid tenantId,
        Guid userId,
        bool isRootAdmin,
        int escalationDays,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            WITH RawPendingRequests AS
            (
                SELECT request.*,
                       CASE request.ScopeType
                           WHEN N'Organization' THEN 1
                           WHEN N'Project' THEN 2
                           ELSE 3
                       END AS MaximumStage,
                       DATEDIFF(hour, request.SubmittedUtc, SYSUTCDATETIME()) /
                           (24 * @EscalationDays) AS ElapsedStage
                FROM dbo.BiAgentAccessRequests AS request
                WHERE request.Status = N'Pending'
            ),
            PendingRequests AS
            (
                SELECT request.*,
                       CASE WHEN request.ElapsedStage > request.MaximumStage
                            THEN request.MaximumStage ELSE request.ElapsedStage END AS CurrentStage
                FROM RawPendingRequests AS request
            )
            SELECT request.AccessRequestId, requester.DisplayName, requester.EmailAddress,
                   request.ScopeType, COALESCE(request.OrganizationId, request.ProjectId, request.TeamId),
                   CASE request.ScopeType
                       WHEN N'Organization' THEN organization.Name
                       WHEN N'Project' THEN CONCAT(parentOrganization.Name, N' / ', project.Name)
                       ELSE CONCAT(teamOrganization.Name, N' / ', parentProject.Name, N' / ', team.Name)
                   END,
                   request.RequestedRole, request.Message, request.Status, request.ResolutionMessage,
                   responder.DisplayName, request.SubmittedUtc, request.ResolvedUtc
            FROM PendingRequests AS request
            INNER JOIN dbo.BiAgentApplicationUsers AS requester
              ON requester.UserId = request.UserId AND requester.TenantId = @TenantId
            LEFT JOIN dbo.BiAgentOrganizations AS organization ON organization.OrganizationId = request.OrganizationId
            LEFT JOIN dbo.BiAgentProjects AS project ON project.ProjectId = request.ProjectId
            LEFT JOIN dbo.BiAgentOrganizations AS parentOrganization ON parentOrganization.OrganizationId = project.OrganizationId
            LEFT JOIN dbo.BiAgentTeams AS team ON team.TeamId = request.TeamId
            LEFT JOIN dbo.BiAgentProjects AS parentProject ON parentProject.ProjectId = team.ProjectId
            LEFT JOIN dbo.BiAgentOrganizations AS teamOrganization ON teamOrganization.OrganizationId = parentProject.OrganizationId
            LEFT JOIN dbo.BiAgentApplicationUsers AS responder ON responder.UserId = request.RespondedByUserId
            WHERE request.UserId <> @UserId
              AND (@IsRootAdmin = 1 OR EXISTS
            (
                SELECT 1
                FROM dbo.BiAgentUserScopeAssignments AS assignment
                WHERE assignment.UserId = @UserId
                  AND
                  (
                      (request.ScopeType = N'Organization'
                       AND assignment.OrganizationId = request.OrganizationId
                       AND assignment.RoleName = N'OrgLead'
                       AND request.CurrentStage >= 0)
                      OR (request.ScopeType = N'Project'
                       AND assignment.ProjectId = request.ProjectId
                       AND assignment.RoleName = N'ProjectLead'
                       AND request.CurrentStage >= 0)
                      OR (request.ScopeType = N'Project'
                       AND assignment.OrganizationId = project.OrganizationId
                       AND assignment.RoleName = N'OrgLead'
                       AND request.CurrentStage >= 1)
                      OR (request.ScopeType = N'Team'
                       AND assignment.TeamId = request.TeamId
                       AND assignment.RoleName = N'TeamLead'
                       AND request.CurrentStage >= 0)
                      OR (request.ScopeType = N'Team'
                       AND assignment.ProjectId = parentProject.ProjectId
                       AND assignment.RoleName = N'ProjectLead'
                       AND request.CurrentStage >= 1)
                      OR (request.ScopeType = N'Team'
                       AND assignment.OrganizationId = parentProject.OrganizationId
                       AND assignment.RoleName = N'OrgLead'
                       AND request.CurrentStage >= 2)
                  ))
                  AND request.CurrentStage <= request.MaximumStage
            )
            ORDER BY request.SubmittedUtc;
            """,
            connection);
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@IsRootAdmin", SqlDbType.Bit).Value = isRootAdmin;
        command.Parameters.Add("@EscalationDays", SqlDbType.Int).Value = escalationDays;
        return await ReadRequestsAsync(command, cancellationToken);
    }

    public async Task<AccessRequestMutationResult> CreateAsync(
        Guid tenantId,
        Guid userId,
        string scopeType,
        Guid scopeId,
        string roleName,
        string? message,
        CancellationToken cancellationToken)
    {
        var normalizedScope = scopeType.Trim();
        var normalizedRole = roleName.Trim();
        if (scopeId == Guid.Empty || !IsValidScopeRole(normalizedScope, normalizedRole))
        {
            return AccessRequestMutationResult.Invalid;
        }

        var requestId = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await using var command = new SqlCommand(
            """
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.BiAgentApplicationUsers WITH (UPDLOCK, HOLDLOCK)
                WHERE UserId = @UserId AND TenantId = @TenantId
            )
                SELECT 2;
            ELSE IF NOT EXISTS
            (
                SELECT 1
                FROM
                (
                    SELECT N'Organization' AS ScopeType, OrganizationId AS ScopeId
                    FROM dbo.BiAgentOrganizations WHERE IsEnabled = 1
                    UNION ALL
                    SELECT N'Project', ProjectId
                    FROM dbo.BiAgentProjects AS project
                    INNER JOIN dbo.BiAgentOrganizations AS organization
                      ON organization.OrganizationId = project.OrganizationId AND organization.IsEnabled = 1
                    WHERE project.IsEnabled = 1
                    UNION ALL
                    SELECT N'Team', TeamId
                    FROM dbo.BiAgentTeams AS team
                    INNER JOIN dbo.BiAgentProjects AS project
                      ON project.ProjectId = team.ProjectId AND project.IsEnabled = 1
                    INNER JOIN dbo.BiAgentOrganizations AS organization
                      ON organization.OrganizationId = project.OrganizationId AND organization.IsEnabled = 1
                    WHERE team.IsEnabled = 1
                ) AS scope
                WHERE scope.ScopeType = @ScopeType AND scope.ScopeId = @ScopeId
            )
                SELECT 1;
            ELSE IF EXISTS
            (
                SELECT 1 FROM dbo.BiAgentAccessRequests WITH (UPDLOCK, HOLDLOCK)
                WHERE UserId = @UserId AND Status = N'Pending'
                  AND ScopeType = @ScopeType AND RequestedRole = @RoleName
                  AND COALESCE(OrganizationId, ProjectId, TeamId) = @ScopeId
            )
                SELECT 3;
            ELSE
            BEGIN
                INSERT INTO dbo.BiAgentAccessRequests
                    (AccessRequestId, UserId, ScopeType, OrganizationId, ProjectId, TeamId,
                     RequestedRole, Message)
                VALUES
                    (@RequestId, @UserId, @ScopeType,
                     CASE WHEN @ScopeType = N'Organization' THEN @ScopeId END,
                     CASE WHEN @ScopeType = N'Project' THEN @ScopeId END,
                     CASE WHEN @ScopeType = N'Team' THEN @ScopeId END,
                     @RoleName, @Message);
                SELECT 0;
            END
            """,
            connection,
            transaction);
        command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        command.Parameters.Add("@ScopeType", SqlDbType.NVarChar, 20).Value = normalizedScope;
        command.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value = scopeId;
        command.Parameters.Add("@RoleName", SqlDbType.NVarChar, 20).Value = normalizedRole;
        AddNullableText(command, "@Message", message, 2000);
        try
        {
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return result switch
            {
                0 => AccessRequestMutationResult.Updated,
                1 => AccessRequestMutationResult.NotFound,
                2 => AccessRequestMutationResult.Forbidden,
                _ => AccessRequestMutationResult.Duplicate
            };
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccessRequestMutationResult.Duplicate;
        }
    }

    public async Task<AccessRequestMutationResult> DecideAsync(
        Guid tenantId,
        Guid actorUserId,
        Guid accessRequestId,
        bool isRootAdmin,
        string decision,
        string? message,
        int escalationDays,
        CancellationToken cancellationToken)
    {
        if (decision is not ("Approved" or "Declined"))
        {
            return AccessRequestMutationResult.Invalid;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        Guid requesterId;
        string scopeType;
        Guid scopeId;
        string roleName;
        bool requesterHadAccess;
        int elapsedStage;
        int maximumStage;
        Guid parentProjectId;
        Guid organizationId;
        await using (var command = new SqlCommand(
            """
            SELECT request.UserId, request.ScopeType,
                   COALESCE(request.OrganizationId, request.ProjectId, request.TeamId),
                   request.RequestedRole, requester.IsEnabled, request.Message, request.SubmittedUtc,
                   request.Status,
                   CASE request.ScopeType WHEN N'Organization' THEN 1 WHEN N'Project' THEN 2 ELSE 3 END,
                   DATEDIFF(hour, request.SubmittedUtc, SYSUTCDATETIME()) / (24 * @EscalationDays),
                   scopedProject.ProjectId, scopedProject.OrganizationId
            FROM dbo.BiAgentAccessRequests AS request WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.BiAgentApplicationUsers AS requester ON requester.UserId = request.UserId
            LEFT JOIN dbo.BiAgentTeams AS team ON team.TeamId = request.TeamId
            LEFT JOIN dbo.BiAgentProjects AS scopedProject
              ON scopedProject.ProjectId = COALESCE(request.ProjectId, team.ProjectId)
            WHERE request.AccessRequestId = @RequestId
              AND requester.TenantId = @TenantId;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = accessRequestId;
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            command.Parameters.Add("@EscalationDays", SqlDbType.Int).Value = escalationDays;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AccessRequestMutationResult.NotFound;
                }

                requesterId = reader.GetGuid(0);
                scopeType = reader.GetString(1);
                scopeId = reader.GetGuid(2);
                roleName = reader.GetString(3);
                requesterHadAccess = reader.GetBoolean(4);
                if (reader.GetString(7) != "Pending")
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return AccessRequestMutationResult.AlreadyResolved;
                }

                maximumStage = reader.GetInt32(8);
                elapsedStage = Math.Min(reader.GetInt32(9), maximumStage);
                parentProjectId = reader.IsDBNull(10) ? Guid.Empty : reader.GetGuid(10);
                organizationId = reader.IsDBNull(11) ? Guid.Empty : reader.GetGuid(11);
            }
        }

        if (requesterId == actorUserId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccessRequestMutationResult.Forbidden;
        }

        if (!isRootAdmin && !await ActorCanDecideAsync(
                connection,
                transaction,
                actorUserId,
                scopeType,
                scopeId,
                parentProjectId,
                organizationId,
                elapsedStage,
                cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccessRequestMutationResult.Forbidden;
        }

        var normalizedMessage = Limit(message, 2000);
        if (decision == "Approved")
        {
            await using var assignCommand = new SqlCommand(
                """
                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentUserScopeAssignments WITH (UPDLOCK, HOLDLOCK)
                    WHERE UserId = @UserId AND ScopeType = @ScopeType
                      AND RoleName = @RoleName
                      AND COALESCE(OrganizationId, ProjectId, TeamId) = @ScopeId
                )
                BEGIN
                    INSERT INTO dbo.BiAgentUserScopeAssignments
                        (AssignmentId, UserId, ScopeType, OrganizationId, ProjectId, TeamId,
                         RoleName, AssignedByUserId)
                    VALUES
                        (@AssignmentId, @UserId, @ScopeType,
                         CASE WHEN @ScopeType = N'Organization' THEN @ScopeId END,
                         CASE WHEN @ScopeType = N'Project' THEN @ScopeId END,
                         CASE WHEN @ScopeType = N'Team' THEN @ScopeId END,
                         @RoleName, @ActorUserId);
                END;
                UPDATE dbo.BiAgentApplicationUsers SET IsEnabled = 1 WHERE UserId = @UserId;
                """,
                connection,
                transaction);
            assignCommand.Parameters.Add("@AssignmentId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            assignCommand.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = requesterId;
            assignCommand.Parameters.Add("@ScopeType", SqlDbType.NVarChar, 20).Value = scopeType;
            assignCommand.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value = scopeId;
            assignCommand.Parameters.Add("@RoleName", SqlDbType.NVarChar, 20).Value = roleName;
            assignCommand.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            await assignCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateCommand = new SqlCommand(
            """
            UPDATE dbo.BiAgentAccessRequests
            SET Status = @Decision,
                ResolutionMessage = @Message,
                RespondedByUserId = @ActorUserId,
                ResolvedUtc = SYSUTCDATETIME()
            WHERE AccessRequestId = @RequestId AND Status = N'Pending';
            """,
            connection,
            transaction))
        {
            updateCommand.Parameters.Add("@Decision", SqlDbType.NVarChar, 20).Value = decision;
            AddNullableText(updateCommand, "@Message", normalizedMessage, 2000);
            updateCommand.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            updateCommand.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = accessRequestId;
            if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return AccessRequestMutationResult.AlreadyResolved;
            }
        }

        var template = decision == "Declined"
            ? "AccessRequestDeclined"
            : requesterHadAccess ? "AccessAdjustmentApproved" : "OnboardingApproved";
        await using (var notificationCommand = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentAccessRequestNotifications
                (NotificationId, AccessRequestId, RecipientUserId, TemplateKey, NotificationDate)
            VALUES
                (@NotificationId, @RequestId, @RecipientUserId, @TemplateKey, CONVERT(date, SYSUTCDATETIME()));
            """,
            connection,
            transaction))
        {
            notificationCommand.Parameters.Add("@NotificationId", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            notificationCommand.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = accessRequestId;
            notificationCommand.Parameters.Add("@RecipientUserId", SqlDbType.UniqueIdentifier).Value = requesterId;
            notificationCommand.Parameters.Add("@TemplateKey", SqlDbType.NVarChar, 40).Value = template;
            await notificationCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return AccessRequestMutationResult.Updated;
    }

    public async Task<AccessRequestSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT EscalationDays FROM dbo.BiAgentAccessRequestSettings WHERE SettingsId = 1;",
            connection);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return new AccessRequestSettings(Convert.ToInt32(result));
    }

    public async Task SetSettingsAsync(
        Guid actorUserId,
        int escalationDays,
        CancellationToken cancellationToken)
    {
        if (escalationDays is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(escalationDays), "Escalation delay must be between 1 and 30 days.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            UPDATE dbo.BiAgentAccessRequestSettings
            SET EscalationDays = @EscalationDays, UpdatedUtc = SYSUTCDATETIME(), UpdatedByUserId = @ActorUserId
            WHERE SettingsId = 1;
            """,
            connection);
        command.Parameters.Add("@EscalationDays", SqlDbType.Int).Value = escalationDays;
        command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The access request escalation settings were not initialized.");
        }
    }

    public async Task<int> QueueDailyNotificationsAsync(
        int escalationDays,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            WITH RawPendingRequests AS
            (
                SELECT request.AccessRequestId, request.UserId, request.ScopeType,
                       request.OrganizationId, request.ProjectId, request.TeamId, request.SubmittedUtc,
                       CASE request.ScopeType WHEN N'Organization' THEN 1 WHEN N'Project' THEN 2 ELSE 3 END AS MaximumStage,
                       DATEDIFF(hour, request.SubmittedUtc, SYSUTCDATETIME()) / (24 * @EscalationDays) AS ElapsedStage
                FROM dbo.BiAgentAccessRequests AS request
                WHERE request.Status = N'Pending'
            ),
            PendingRequests AS
            (
                SELECT request.*,
                       CASE WHEN request.ElapsedStage > request.MaximumStage
                            THEN request.MaximumStage ELSE request.ElapsedStage END AS CurrentStage
                FROM RawPendingRequests AS request
            ),
            Targets AS
            (
                SELECT request.AccessRequestId, assignment.UserId AS RecipientUserId, 0 AS Stage
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON request.ScopeType = N'Organization'
                 AND assignment.OrganizationId = request.OrganizationId AND assignment.RoleName = N'OrgLead'
                UNION ALL
                SELECT request.AccessRequestId, assignment.UserId, 0
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON request.ScopeType = N'Project'
                 AND assignment.ProjectId = request.ProjectId AND assignment.RoleName = N'ProjectLead'
                UNION ALL
                SELECT request.AccessRequestId, assignment.UserId, 1
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = request.ProjectId
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON request.ScopeType = N'Project'
                 AND assignment.OrganizationId = project.OrganizationId AND assignment.RoleName = N'OrgLead'
                UNION ALL
                SELECT request.AccessRequestId, assignment.UserId, 0
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON request.ScopeType = N'Team'
                 AND assignment.TeamId = request.TeamId AND assignment.RoleName = N'TeamLead'
                UNION ALL
                SELECT request.AccessRequestId, assignment.UserId, 1
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentTeams AS team ON team.TeamId = request.TeamId
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON request.ScopeType = N'Team'
                 AND assignment.ProjectId = team.ProjectId AND assignment.RoleName = N'ProjectLead'
                UNION ALL
                SELECT request.AccessRequestId, assignment.UserId, 2
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentTeams AS team ON team.TeamId = request.TeamId
                INNER JOIN dbo.BiAgentProjects AS project ON project.ProjectId = team.ProjectId
                INNER JOIN dbo.BiAgentUserScopeAssignments AS assignment
                  ON request.ScopeType = N'Team'
                 AND assignment.OrganizationId = project.OrganizationId AND assignment.RoleName = N'OrgLead'
                UNION ALL
                SELECT request.AccessRequestId, role.UserId, request.MaximumStage
                FROM PendingRequests AS request
                INNER JOIN dbo.BiAgentApplicationUserRoles AS role ON role.RoleName = N'ApplicationAdmin'
            ),
            DeduplicatedTargets AS
            (
                SELECT AccessRequestId, RecipientUserId, MIN(Stage) AS Stage
                FROM Targets
                GROUP BY AccessRequestId, RecipientUserId
            )
            INSERT INTO dbo.BiAgentAccessRequestNotifications
                (NotificationId, AccessRequestId, RecipientUserId, TemplateKey, NotificationDate)
            SELECT NEWID(), request.AccessRequestId, target.RecipientUserId,
                   N'AccessRequestReminder', CONVERT(date, SYSUTCDATETIME())
            FROM PendingRequests AS request
            INNER JOIN DeduplicatedTargets AS target ON target.AccessRequestId = request.AccessRequestId
            INNER JOIN dbo.BiAgentApplicationUsers AS recipient
              ON recipient.UserId = target.RecipientUserId AND recipient.IsEnabled = 1
            WHERE target.RecipientUserId <> request.UserId
              AND target.Stage <= request.CurrentStage
              AND NOT EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentAccessRequestNotifications AS existing WITH (UPDLOCK, HOLDLOCK)
                  WHERE existing.AccessRequestId = request.AccessRequestId
                    AND existing.RecipientUserId = target.RecipientUserId
                    AND existing.TemplateKey = N'AccessRequestReminder'
                    AND existing.NotificationDate = CONVERT(date, SYSUTCDATETIME())
              );
            SELECT @@ROWCOUNT;
            """,
            connection);
        command.Parameters.Add("@EscalationDays", SqlDbType.Int).Value = escalationDays;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<AccessRequestEmail>> GetPendingEmailsAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var emails = new List<AccessRequestEmail>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SET NOCOUNT ON;
            DECLARE @Claimed TABLE (NotificationId uniqueidentifier NOT NULL PRIMARY KEY);
            ;WITH Candidates AS
            (
                SELECT TOP (@BatchSize) notification.NotificationId
                FROM dbo.BiAgentAccessRequestNotifications AS notification WITH (UPDLOCK, READPAST, ROWLOCK)
                INNER JOIN dbo.BiAgentApplicationUsers AS recipient
                  ON recipient.UserId = notification.RecipientUserId
                 AND recipient.IsEnabled = 1 AND recipient.EmailAddress IS NOT NULL
                WHERE notification.SentUtc IS NULL
                  AND (notification.LastAttemptUtc IS NULL
                    OR notification.LastAttemptUtc < DATEADD(minute, -10, SYSUTCDATETIME()))
                ORDER BY notification.CreatedUtc, notification.NotificationId
            )
            UPDATE notification
            SET LastAttemptUtc = SYSUTCDATETIME()
            OUTPUT inserted.NotificationId INTO @Claimed
            FROM dbo.BiAgentAccessRequestNotifications AS notification
            INNER JOIN Candidates AS candidate ON candidate.NotificationId = notification.NotificationId;

            SELECT notification.NotificationId, notification.TemplateKey,
                   recipient.DisplayName, recipient.EmailAddress,
                   requester.DisplayName, requester.EmailAddress, request.ScopeType,
                   CASE request.ScopeType
                       WHEN N'Organization' THEN organization.Name
                       WHEN N'Project' THEN CONCAT(parentOrganization.Name, N' / ', project.Name)
                       ELSE CONCAT(teamOrganization.Name, N' / ', parentProject.Name, N' / ', team.Name)
                   END,
                   request.RequestedRole, request.Message, request.ResolutionMessage, request.SubmittedUtc
            FROM @Claimed AS claimed
            INNER JOIN dbo.BiAgentAccessRequestNotifications AS notification
              ON notification.NotificationId = claimed.NotificationId
            INNER JOIN dbo.BiAgentAccessRequests AS request ON request.AccessRequestId = notification.AccessRequestId
            INNER JOIN dbo.BiAgentApplicationUsers AS recipient
              ON recipient.UserId = notification.RecipientUserId AND recipient.IsEnabled = 1
            INNER JOIN dbo.BiAgentApplicationUsers AS requester ON requester.UserId = request.UserId
            LEFT JOIN dbo.BiAgentOrganizations AS organization ON organization.OrganizationId = request.OrganizationId
            LEFT JOIN dbo.BiAgentProjects AS project ON project.ProjectId = request.ProjectId
            LEFT JOIN dbo.BiAgentOrganizations AS parentOrganization ON parentOrganization.OrganizationId = project.OrganizationId
            LEFT JOIN dbo.BiAgentTeams AS team ON team.TeamId = request.TeamId
            LEFT JOIN dbo.BiAgentProjects AS parentProject ON parentProject.ProjectId = team.ProjectId
            LEFT JOIN dbo.BiAgentOrganizations AS teamOrganization ON teamOrganization.OrganizationId = parentProject.OrganizationId
            ORDER BY notification.CreatedUtc;
            """,
            connection);
        command.Parameters.Add("@BatchSize", SqlDbType.Int).Value = batchSize;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            emails.Add(new AccessRequestEmail(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? "Unavailable scope" : reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetDateTimeOffset(11)));
        }

        return emails;
    }

    public async Task RecordEmailAttemptAsync(
        Guid notificationId,
        bool sent,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            UPDATE dbo.BiAgentAccessRequestNotifications
            SET AttemptCount = AttemptCount + 1,
                LastAttemptUtc = SYSUTCDATETIME(),
                SentUtc = CASE WHEN @Sent = 1 THEN SYSUTCDATETIME() ELSE SentUtc END,
                LastError = @Error
            WHERE NotificationId = @NotificationId;
            """,
            connection);
        command.Parameters.Add("@NotificationId", SqlDbType.UniqueIdentifier).Value = notificationId;
        command.Parameters.Add("@Sent", SqlDbType.Bit).Value = sent;
        AddNullableText(command, "@Error", Limit(error, 2000), 2000);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AccessRequestRecord>> ReadRequestsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var requests = new List<AccessRequestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(new AccessRequestRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? "Unavailable scope" : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetDateTimeOffset(11),
                reader.IsDBNull(12) ? null : reader.GetDateTimeOffset(12)));
        }

        return requests;
    }

    private static async Task<bool> ActorCanDecideAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid actorUserId,
        string scopeType,
        Guid scopeId,
        Guid parentProjectId,
        Guid organizationId,
        int elapsedStage,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT CONVERT(bit, CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.BiAgentUserScopeAssignments AS assignment
                WHERE assignment.UserId = @ActorUserId
                  AND
                  (
                      (@ScopeType = N'Organization' AND assignment.OrganizationId = @ScopeId
                          AND assignment.RoleName = N'OrgLead' AND @Stage >= 0)
                      OR (@ScopeType = N'Project' AND assignment.ProjectId = @ScopeId
                          AND assignment.RoleName = N'ProjectLead' AND @Stage >= 0)
                      OR (@ScopeType = N'Project' AND assignment.OrganizationId = @OrganizationId
                          AND assignment.RoleName = N'OrgLead' AND @Stage >= 1)
                      OR (@ScopeType = N'Team' AND assignment.TeamId = @ScopeId
                          AND assignment.RoleName = N'TeamLead' AND @Stage >= 0)
                      OR (@ScopeType = N'Team' AND assignment.ProjectId = @ProjectId
                          AND assignment.RoleName = N'ProjectLead' AND @Stage >= 1)
                      OR (@ScopeType = N'Team' AND assignment.OrganizationId = @OrganizationId
                          AND assignment.RoleName = N'OrgLead' AND @Stage >= 2)
                  )
            ) THEN 1 ELSE 0 END);
            """,
            connection,
            transaction);
        command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
        command.Parameters.Add("@ScopeType", SqlDbType.NVarChar, 20).Value = scopeType;
        command.Parameters.Add("@ScopeId", SqlDbType.UniqueIdentifier).Value = scopeId;
        command.Parameters.Add("@ProjectId", SqlDbType.UniqueIdentifier).Value = parentProjectId;
        command.Parameters.Add("@OrganizationId", SqlDbType.UniqueIdentifier).Value = organizationId;
        command.Parameters.Add("@Stage", SqlDbType.Int).Value = elapsedStage;
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static bool IsValidScopeRole(string scopeType, string roleName) =>
        (scopeType, roleName) is
            ("Organization", "OrgLead" or "ReadOnly") or
            ("Project", "ProjectLead" or "ReadOnly") or
            ("Team", "TeamLead" or "ReadOnly");

    private static void AddNullableText(SqlCommand command, string name, string? value, int size)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.NVarChar, size);
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    private static string? Limit(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
