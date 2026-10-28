using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.GovernanceAudit;

public sealed class SqlGovernanceAuditConfigurationStore(string connectionString)
    : IGovernanceAuditConfigurationStore
{
    public async Task<GovernanceAuditConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        int version;
        DateTime updatedUtc;
        string updatedBy;
        await using (var command = new SqlCommand(
            """
            SELECT Version, UpdatedUtc, UpdatedBy
            FROM dbo.BiAgentGovernanceAuditConfiguration
            WHERE ConfigurationId = 1;
            """,
            connection,
            transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Governance Audit configuration is missing.");
            }

            version = reader.GetInt32(0);
            updatedUtc = reader.GetDateTime(1);
            updatedBy = reader.GetString(2);
        }

        var workspaceIds = new List<Guid>();
        await using (var command = new SqlCommand(
            """
            SELECT WorkspaceId
            FROM dbo.BiAgentGovernanceAuditWorkspaces
            WHERE ConfigurationId = 1
            ORDER BY WorkspaceId;
            """,
            connection,
            transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                workspaceIds.Add(reader.GetGuid(0));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new GovernanceAuditConfiguration(
            version,
            workspaceIds.ToArray(),
            new DateTimeOffset(DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc)),
            updatedBy);
    }

    public async Task<GovernanceAuditConfiguration?> UpdateAsync(
        Guid[] workspaceIds,
        int expectedVersion,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        int version;
        DateTime updatedUtc;
        string changedBy;
        await using (var command = new SqlCommand(
            """
            UPDATE dbo.BiAgentGovernanceAuditConfiguration
            SET Version = Version + 1,
                UpdatedUtc = SYSUTCDATETIME(),
                UpdatedBy = @UpdatedBy
            OUTPUT INSERTED.Version, INSERTED.UpdatedUtc, INSERTED.UpdatedBy
            WHERE ConfigurationId = 1 AND Version = @ExpectedVersion;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 256).Value = updatedBy;
            command.Parameters.Add("@ExpectedVersion", SqlDbType.Int).Value = expectedVersion;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            version = reader.GetInt32(0);
            updatedUtc = reader.GetDateTime(1);
            changedBy = reader.GetString(2);
        }

        await using (var delete = new SqlCommand(
            "DELETE FROM dbo.BiAgentGovernanceAuditWorkspaces WHERE ConfigurationId = 1;",
            connection,
            transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentGovernanceAuditWorkspaces (ConfigurationId, WorkspaceId)
            VALUES (1, @WorkspaceId);
            """,
            connection,
            transaction))
        {
            var parameter = insert.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier);
            foreach (var workspaceId in workspaceIds)
            {
                parameter.Value = workspaceId;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        long auditId;
        await using (var audit = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentGovernanceAuditConfigurationAudit (Version, ChangedUtc, ChangedBy)
            VALUES (@Version, @ChangedUtc, @ChangedBy);
            SELECT CAST(SCOPE_IDENTITY() AS bigint);
            """,
            connection,
            transaction))
        {
            audit.Parameters.Add("@Version", SqlDbType.Int).Value = version;
            audit.Parameters.Add("@ChangedUtc", SqlDbType.DateTime2).Value = updatedUtc;
            audit.Parameters.Add("@ChangedBy", SqlDbType.NVarChar, 256).Value = changedBy;
            auditId = (long)(await audit.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidDataException("Governance Audit configuration history could not be written."));
        }

        await using (var auditWorkspace = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentGovernanceAuditConfigurationAuditWorkspaces (AuditId, WorkspaceId)
            VALUES (@AuditId, @WorkspaceId);
            """,
            connection,
            transaction))
        {
            var auditIdParameter = auditWorkspace.Parameters.Add("@AuditId", SqlDbType.BigInt);
            var workspaceIdParameter = auditWorkspace.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier);
            auditIdParameter.Value = auditId;
            foreach (var workspaceId in workspaceIds.Order())
            {
                workspaceIdParameter.Value = workspaceId;
                await auditWorkspace.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new GovernanceAuditConfiguration(
            version,
            workspaceIds.Order().ToArray(),
            new DateTimeOffset(DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc)),
            changedBy);
    }
}
