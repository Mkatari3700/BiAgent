using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Security;

public interface IApplicationUserStore
{
    Task<ApplicationUserSession> EnsureUserAsync(
        Guid tenantId,
        Guid objectId,
        string? displayName,
        string? emailAddress,
        Guid bootstrapAdminObjectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ApplicationUserSummary>> GetUsersAsync(
        Guid tenantId,
        Guid bootstrapAdminObjectId,
        CancellationToken cancellationToken);

    Task<ApplicationUserRoleChangeResult> SetAdminAsync(
        Guid tenantId,
        Guid targetUserId,
        Guid actorUserId,
        bool isAdmin,
        Guid bootstrapAdminObjectId,
        CancellationToken cancellationToken);

    Task<ApplicationUserRoleChangeResult> SetReviewerRoleAsync(
        Guid tenantId,
        Guid targetUserId,
        Guid actorUserId,
        string roleName,
        bool isGranted,
        CancellationToken cancellationToken);
}

public sealed record ApplicationUserSession(
    Guid UserId,
    Guid TenantId,
    Guid ObjectId,
    string DisplayName,
    string? EmailAddress,
    bool IsAdmin,
    bool IsManager,
    bool IsLead,
    bool IsEnabled,
    bool IsBootstrapAdmin,
    DateTimeOffset FirstSeenUtc)
{
    public bool IsOrgLead { get; init; }
    public bool IsProjectLead { get; init; }
    public bool IsTeamLead { get; init; }
}

/// <summary>Represents a signed-in BI Sentinel user and their in-app role.</summary>
public sealed record ApplicationUserSummary(
    Guid UserId,
    string DisplayName,
    string? EmailAddress,
    bool IsAdmin,
    bool IsManager,
    bool IsLead,
    bool IsBootstrapAdmin,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

/// <summary>Reports the current sign-in state and BI Sentinel application role.</summary>
public sealed record ApplicationAuthSessionResponse(
    bool AuthenticationEnabled,
    bool IsAuthenticated,
    string? DisplayName,
    string? EmailAddress,
    bool IsAdmin,
    bool IsManager,
    bool IsLead,
    bool IsBootstrapAdmin)
{
    public bool IsOrgLead { get; init; }
    public bool IsProjectLead { get; init; }
    public bool IsTeamLead { get; init; }
    public bool AccessGranted { get; init; }
}

/// <summary>Specifies the administrator status to assign to an application user.</summary>
public sealed record SetApplicationUserAdminRequest
{
    public required bool IsAdmin { get; init; }
}

/// <summary>Reports the application user's updated administrator status.</summary>
public sealed record ApplicationUserAdminResponse(Guid UserId, bool IsAdmin);

public sealed record SetApplicationUserReviewRoleRequest
{
    public required string RoleName { get; init; }

    public required bool IsGranted { get; init; }
}

public sealed record ApplicationUserReviewRoleResponse(Guid UserId, string RoleName, bool IsGranted);

public enum ApplicationUserRoleChangeResult
{
    Updated,
    Unchanged,
    UserNotFound,
    BootstrapAdminCannotBeDemoted,
    LastAdminCannotBeDemoted
}

public sealed class SqlApplicationUserStore(string connectionString) : IApplicationUserStore
{
    public async Task<ApplicationUserSession> EnsureUserAsync(
        Guid tenantId,
        Guid objectId,
        string? displayName,
        string? emailAddress,
        Guid bootstrapAdminObjectId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        Guid userId;
        bool isEnabled;
        bool isNewUser;
        DateTimeOffset firstSeenUtc;
        var normalizedName = Limit(displayName, 256) ?? Limit(emailAddress, 256) ?? objectId.ToString();
        var normalizedEmail = Limit(emailAddress, 256);

        await using (var command = new SqlCommand(
            """
            SELECT UserId, IsEnabled, FirstSeenUtc
            FROM dbo.BiAgentApplicationUsers WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND EntraObjectId = @EntraObjectId;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            command.Parameters.Add("@EntraObjectId", SqlDbType.UniqueIdentifier).Value = objectId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                userId = reader.GetGuid(0);
                isEnabled = reader.GetBoolean(1);
                firstSeenUtc = reader.GetDateTimeOffset(2);
                isNewUser = false;
            }
            else
            {
                userId = Guid.NewGuid();
                isEnabled = objectId == bootstrapAdminObjectId;
                firstSeenUtc = DateTimeOffset.MinValue;
                isNewUser = true;
            }
        }

        if (isNewUser)
        {
            await using var command = new SqlCommand(
                """
                INSERT INTO dbo.BiAgentApplicationUsers
                    (UserId, TenantId, EntraObjectId, DisplayName, EmailAddress, IsEnabled)
                OUTPUT inserted.FirstSeenUtc
                VALUES
                    (@UserId, @TenantId, @EntraObjectId, @DisplayName, @EmailAddress, @IsEnabled);
                """,
                connection,
                transaction);
            command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            command.Parameters.Add("@EntraObjectId", SqlDbType.UniqueIdentifier).Value = objectId;
            AddNullableText(command, "@DisplayName", normalizedName);
            AddNullableText(command, "@EmailAddress", normalizedEmail);
            command.Parameters.Add("@IsEnabled", SqlDbType.Bit).Value = isEnabled;
            firstSeenUtc = (DateTimeOffset)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The application user's creation time could not be read."));
        }
        else
        {
            await using var command = new SqlCommand(
                """
                UPDATE dbo.BiAgentApplicationUsers
                SET DisplayName = @DisplayName,
                    EmailAddress = @EmailAddress,
                    LastSeenUtc = SYSDATETIMEOFFSET()
                WHERE UserId = @UserId;
                """,
                connection,
                transaction);
            command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
            AddNullableText(command, "@DisplayName", normalizedName);
            AddNullableText(command, "@EmailAddress", normalizedEmail);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var isBootstrapAdmin = objectId == bootstrapAdminObjectId;
        if (isBootstrapAdmin)
        {
            await using var command = new SqlCommand(
                """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.BiAgentApplicationUserRoles
                    WHERE UserId = @UserId AND RoleName = N'ApplicationAdmin'
                )
                BEGIN
                    INSERT INTO dbo.BiAgentApplicationUserRoles
                        (UserId, RoleName, AssignedByUserId)
                    VALUES
                        (@UserId, N'ApplicationAdmin', NULL);

                    INSERT INTO dbo.BiAgentApplicationRoleAudit
                        (TargetUserId, RoleName, IsGranted, ChangedByUserId)
                    VALUES
                        (@UserId, N'ApplicationAdmin', 1, @UserId);
                END;
                """,
                connection,
                transaction);
            command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        bool isAdmin;
        bool isManager;
        bool isLead;
        bool isOrgLead;
        bool isProjectLead;
        bool isTeamLead;
        await using (var command = new SqlCommand(
            """
            SELECT
                CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentApplicationUserRoles
                    WHERE UserId = @UserId AND RoleName = N'ApplicationAdmin'
                ) THEN 1 ELSE 0 END),
                CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentApplicationUserRoles
                    WHERE UserId = @UserId AND RoleName = N'ReviewManager'
                ) THEN 1 ELSE 0 END),
                CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentApplicationUserRoles
                    WHERE UserId = @UserId AND RoleName = N'ReviewLead'
                ) THEN 1 ELSE 0 END),
                CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentUserScopeAssignments
                    WHERE UserId = @UserId AND RoleName = N'OrgLead'
                ) THEN 1 ELSE 0 END),
                CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentUserScopeAssignments
                    WHERE UserId = @UserId AND RoleName = N'ProjectLead'
                ) THEN 1 ELSE 0 END),
                CONVERT(bit, CASE WHEN EXISTS
                (
                    SELECT 1 FROM dbo.BiAgentUserScopeAssignments
                    WHERE UserId = @UserId AND RoleName = N'TeamLead'
                ) THEN 1 ELSE 0 END);
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("The application user roles could not be read.");
                }

                isAdmin = reader.GetBoolean(0);
                isManager = reader.GetBoolean(1);
                isLead = reader.GetBoolean(2);
                isOrgLead = reader.GetBoolean(3);
                isProjectLead = reader.GetBoolean(4);
                isTeamLead = reader.GetBoolean(5);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new ApplicationUserSession(
            userId,
            tenantId,
            objectId,
            normalizedName,
            normalizedEmail,
            isAdmin,
            isManager,
            isLead,
            isEnabled,
            isBootstrapAdmin,
            firstSeenUtc)
        {
            IsOrgLead = isOrgLead,
            IsProjectLead = isProjectLead,
            IsTeamLead = isTeamLead
        };
    }

    public async Task<IReadOnlyList<ApplicationUserSummary>> GetUsersAsync(
        Guid tenantId,
        Guid bootstrapAdminObjectId,
        CancellationToken cancellationToken)
    {
        var users = new List<ApplicationUserSummary>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT u.UserId, u.EntraObjectId, u.DisplayName, u.EmailAddress, u.FirstSeenUtc,
                   u.LastSeenUtc,
                   CONVERT(bit, CASE WHEN adminRole.UserId IS NULL THEN 0 ELSE 1 END) AS IsAdmin,
                   CONVERT(bit, CASE WHEN managerRole.UserId IS NULL THEN 0 ELSE 1 END) AS IsManager,
                   CONVERT(bit, CASE WHEN leadRole.UserId IS NULL THEN 0 ELSE 1 END) AS IsLead
            FROM dbo.BiAgentApplicationUsers AS u
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS adminRole
              ON adminRole.UserId = u.UserId AND adminRole.RoleName = N'ApplicationAdmin'
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS managerRole
              ON managerRole.UserId = u.UserId AND managerRole.RoleName = N'ReviewManager'
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS leadRole
              ON leadRole.UserId = u.UserId AND leadRole.RoleName = N'ReviewLead'
            WHERE u.TenantId = @TenantId AND u.IsEnabled = 1
            ORDER BY u.DisplayName, u.EmailAddress;
            """,
            connection);
        command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new ApplicationUserSummary(
                reader.GetGuid(0),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetGuid(1) == bootstrapAdminObjectId,
                reader.GetDateTimeOffset(4),
                reader.GetDateTimeOffset(5)));
        }

        return users;
    }

    public async Task<ApplicationUserRoleChangeResult> SetAdminAsync(
        Guid tenantId,
        Guid targetUserId,
        Guid actorUserId,
        bool isAdmin,
        Guid bootstrapAdminObjectId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        Guid targetObjectId;
        bool currentlyAdmin;
        await using (var command = new SqlCommand(
            """
            SELECT u.EntraObjectId,
                   CONVERT(bit, CASE WHEN r.UserId IS NULL THEN 0 ELSE 1 END)
            FROM dbo.BiAgentApplicationUsers AS u WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN dbo.BiAgentApplicationUserRoles AS r WITH (UPDLOCK, HOLDLOCK)
              ON r.UserId = u.UserId AND r.RoleName = N'ApplicationAdmin'
            WHERE u.UserId = @TargetUserId AND u.TenantId = @TenantId AND u.IsEnabled = 1;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            command.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ApplicationUserRoleChangeResult.UserNotFound;
            }

            targetObjectId = reader.GetGuid(0);
            currentlyAdmin = reader.GetBoolean(1);
        }

        if (currentlyAdmin == isAdmin)
        {
            await transaction.CommitAsync(cancellationToken);
            return ApplicationUserRoleChangeResult.Unchanged;
        }

        if (!isAdmin && targetObjectId == bootstrapAdminObjectId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ApplicationUserRoleChangeResult.BootstrapAdminCannotBeDemoted;
        }

        if (isAdmin)
        {
            await using var grantCommand = new SqlCommand(
                """
                INSERT INTO dbo.BiAgentApplicationUserRoles
                    (UserId, RoleName, AssignedByUserId)
                VALUES
                    (@TargetUserId, N'ApplicationAdmin', @ActorUserId);
                """,
                connection,
                transaction);
            grantCommand.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            grantCommand.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            await grantCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using (var countCommand = new SqlCommand(
                """
                SELECT COUNT(*)
                FROM dbo.BiAgentApplicationUsers AS u WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.BiAgentApplicationUserRoles AS r WITH (UPDLOCK, HOLDLOCK)
                  ON r.UserId = u.UserId AND r.RoleName = N'ApplicationAdmin'
                WHERE u.TenantId = @TenantId AND u.IsEnabled = 1;
                """,
                connection,
                transaction))
            {
                countCommand.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
                var adminCount = (int)(await countCommand.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("The application administrator count could not be read."));
                if (adminCount <= 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return ApplicationUserRoleChangeResult.LastAdminCannotBeDemoted;
                }
            }

            await using var deleteCommand = new SqlCommand(
                """
                DELETE FROM dbo.BiAgentApplicationUserRoles
                WHERE UserId = @TargetUserId AND RoleName = N'ApplicationAdmin';
                """,
                connection,
                transaction);
            deleteCommand.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentApplicationRoleAudit
                (TargetUserId, RoleName, IsGranted, ChangedByUserId)
            VALUES
                (@TargetUserId, N'ApplicationAdmin', @IsGranted, @ActorUserId);
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            command.Parameters.Add("@IsGranted", SqlDbType.Bit).Value = isAdmin;
            command.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ApplicationUserRoleChangeResult.Updated;
    }

    public async Task<ApplicationUserRoleChangeResult> SetReviewerRoleAsync(
        Guid tenantId,
        Guid targetUserId,
        Guid actorUserId,
        string roleName,
        bool isGranted,
        CancellationToken cancellationToken)
    {
        if (roleName is not ("ReviewManager" or "ReviewLead"))
        {
            throw new ArgumentException("Only ReviewManager and ReviewLead roles can be assigned here.", nameof(roleName));
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await using (var userCommand = new SqlCommand(
            """
            SELECT UserId
            FROM dbo.BiAgentApplicationUsers WITH (UPDLOCK, HOLDLOCK)
            WHERE UserId = @TargetUserId AND TenantId = @TenantId AND IsEnabled = 1;
            """,
            connection,
            transaction))
        {
            userCommand.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            userCommand.Parameters.Add("@TenantId", SqlDbType.UniqueIdentifier).Value = tenantId;
            if (await userCommand.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ApplicationUserRoleChangeResult.UserNotFound;
            }
        }

        await using (var roleCommand = new SqlCommand(
            isGranted
                ? """
                  IF NOT EXISTS
                  (
                      SELECT 1 FROM dbo.BiAgentApplicationUserRoles WITH (UPDLOCK, HOLDLOCK)
                      WHERE UserId = @TargetUserId AND RoleName = @RoleName
                  )
                  BEGIN
                      INSERT INTO dbo.BiAgentApplicationUserRoles
                          (UserId, RoleName, AssignedByUserId)
                      VALUES (@TargetUserId, @RoleName, @ActorUserId);
                      SELECT 1;
                  END
                  ELSE SELECT 0;
                  """
                : """
                  DELETE FROM dbo.BiAgentApplicationUserRoles
                  WHERE UserId = @TargetUserId AND RoleName = @RoleName;
                  SELECT @@ROWCOUNT;
                  """,
            connection,
            transaction))
        {
            roleCommand.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            roleCommand.Parameters.Add("@RoleName", SqlDbType.NVarChar, 64).Value = roleName;
            roleCommand.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            var changed = Convert.ToInt32(await roleCommand.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!changed)
            {
                await transaction.CommitAsync(cancellationToken);
                return ApplicationUserRoleChangeResult.Unchanged;
            }
        }

        await using (var auditCommand = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentApplicationRoleAudit
                (TargetUserId, RoleName, IsGranted, ChangedByUserId)
            VALUES
                (@TargetUserId, @RoleName, @IsGranted, @ActorUserId);
            """,
            connection,
            transaction))
        {
            auditCommand.Parameters.Add("@TargetUserId", SqlDbType.UniqueIdentifier).Value = targetUserId;
            auditCommand.Parameters.Add("@RoleName", SqlDbType.NVarChar, 64).Value = roleName;
            auditCommand.Parameters.Add("@IsGranted", SqlDbType.Bit).Value = isGranted;
            auditCommand.Parameters.Add("@ActorUserId", SqlDbType.UniqueIdentifier).Value = actorUserId;
            await auditCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return ApplicationUserRoleChangeResult.Updated;
    }

    private static void AddNullableText(SqlCommand command, string parameterName, string? value)
    {
        var parameter = command.Parameters.Add(parameterName, SqlDbType.NVarChar, 256);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
