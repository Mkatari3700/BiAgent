using System.Data;
using System.Text.Json;
using BISentinel.Api.GovernanceAudit;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.GovernanceAudit;

public sealed class SqlGovernanceExceptionStore(string connectionString) : IGovernanceExceptionStore
{
    public async Task<GovernanceExceptionRecord> ApproveAsync(
        CreateGovernanceExceptionRequest request,
        string approvedBy,
        CancellationToken cancellationToken)
    {
        var exceptionId = Guid.NewGuid();
        var approvedUtc = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await using (var findingCommand = new SqlCommand(
            """
            SELECT COUNT_BIG(1)
            FROM dbo.BiAgentGovernanceAuditRunFindings WITH (UPDLOCK, HOLDLOCK)
            WHERE RunId = @RunId
              AND WorkspaceId = @WorkspaceId
              AND Code = @CheckId
              AND Status = N'FAIL'
              AND ((ArtifactId = @ArtifactId) OR (ArtifactId IS NULL AND @ArtifactId IS NULL))
              AND ISNULL(ArtifactType, N'') = @ArtifactType
              AND ISNULL(ArtifactName, N'') = @ArtifactName;
            """,
            connection,
            transaction))
        {
            findingCommand.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = request.RunId;
            findingCommand.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = request.WorkspaceId;
            findingCommand.Parameters.Add("@CheckId", SqlDbType.NVarChar, 40).Value = request.CheckId;
            findingCommand.Parameters.Add("@ArtifactId", SqlDbType.UniqueIdentifier).Value =
                (object?)request.ArtifactId ?? DBNull.Value;
            findingCommand.Parameters.Add("@ArtifactType", SqlDbType.NVarChar, 40).Value =
                request.ArtifactType ?? string.Empty;
            findingCommand.Parameters.Add("@ArtifactName", SqlDbType.NVarChar, 256).Value = request.ArtifactName;
            if (Convert.ToInt64(await findingCommand.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                throw new GovernanceExceptionConflictException(
                    "Exceptions can be approved only for a FAIL finding saved in the selected audit run.");
            }
        }

        await using (var activeCommand = new SqlCommand(
            """
            SELECT COUNT_BIG(1)
            FROM dbo.BiAgentGovernanceExceptions WITH (UPDLOCK, HOLDLOCK)
            WHERE WorkspaceId = @WorkspaceId
              AND CheckId = @CheckId
              AND ((ArtifactId = @ArtifactId) OR (ArtifactId IS NULL AND @ArtifactId IS NULL))
              AND ISNULL(ArtifactType, N'') = @ArtifactType
              AND ArtifactName = @ArtifactName
              AND Status = N'Approved'
              AND ExpiresUtc > SYSUTCDATETIME();
            """,
            connection,
            transaction))
        {
            AddFindingKeyParameters(activeCommand, request);
            if (Convert.ToInt64(await activeCommand.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                throw new GovernanceExceptionConflictException(
                    "An active exception already exists for this finding.");
            }
        }

        await using (var insertCommand = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentGovernanceExceptions
                (ExceptionId, WorkspaceId, CheckId, ArtifactId, ArtifactType, ArtifactName, Owner,
                 Reason, ApprovedBy, ApprovedUtc, ExpiresUtc, Status, SourceRunId)
            VALUES
                (@ExceptionId, @WorkspaceId, @CheckId, @ArtifactId, @ArtifactType, @ArtifactName, @Owner,
                 @Reason, @ApprovedBy, @ApprovedUtc, @ExpiresUtc, N'Approved', @SourceRunId);
            """,
            connection,
            transaction))
        {
            insertCommand.Parameters.Add("@ExceptionId", SqlDbType.UniqueIdentifier).Value = exceptionId;
            insertCommand.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = request.WorkspaceId;
            insertCommand.Parameters.Add("@CheckId", SqlDbType.NVarChar, 40).Value = request.CheckId;
            insertCommand.Parameters.Add("@ArtifactId", SqlDbType.UniqueIdentifier).Value =
                (object?)request.ArtifactId ?? DBNull.Value;
            insertCommand.Parameters.Add("@ArtifactType", SqlDbType.NVarChar, 40).Value =
                (object?)request.ArtifactType ?? DBNull.Value;
            insertCommand.Parameters.Add("@ArtifactName", SqlDbType.NVarChar, 256).Value = request.ArtifactName;
            insertCommand.Parameters.Add("@Owner", SqlDbType.NVarChar, 256).Value = request.Owner;
            insertCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 2000).Value = request.Reason;
            insertCommand.Parameters.Add("@ApprovedBy", SqlDbType.NVarChar, 256).Value = approvedBy;
            insertCommand.Parameters.Add("@ApprovedUtc", SqlDbType.DateTimeOffset).Value = approvedUtc;
            insertCommand.Parameters.Add("@ExpiresUtc", SqlDbType.DateTimeOffset).Value = request.ExpiresUtc;
            insertCommand.Parameters.Add("@SourceRunId", SqlDbType.UniqueIdentifier).Value = request.RunId;
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditEventAsync(
            connection,
            transaction,
            exceptionId,
            "Approved",
            approvedUtc,
            approvedBy,
            request.Reason,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new GovernanceExceptionRecord(
            exceptionId,
            request.WorkspaceId,
            request.CheckId,
            request.ArtifactId,
            request.ArtifactType,
            request.ArtifactName,
            request.Owner,
            request.Reason,
            approvedBy,
            approvedUtc,
            request.ExpiresUtc,
            "Approved",
            request.RunId,
            [new GovernanceExceptionAuditEvent("Approved", approvedUtc, approvedBy, request.Reason)]);
    }

    public async Task<GovernanceExceptionRecord?> RevokeAsync(
        Guid exceptionId,
        string reason,
        string revokedBy,
        CancellationToken cancellationToken)
    {
        var revokedUtc = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        GovernanceExceptionRecord? record;
        await using (var selectCommand = new SqlCommand(
            """
            SELECT ExceptionId, WorkspaceId, CheckId, ArtifactId, ArtifactType, ArtifactName, Owner, Reason,
                   ApprovedBy, ApprovedUtc, ExpiresUtc, Status, SourceRunId
            FROM dbo.BiAgentGovernanceExceptions WITH (UPDLOCK, HOLDLOCK)
            WHERE ExceptionId = @ExceptionId;
            """,
            connection,
            transaction))
        {
            selectCommand.Parameters.Add("@ExceptionId", SqlDbType.UniqueIdentifier).Value = exceptionId;
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            record = await reader.ReadAsync(cancellationToken)
                ? ReadRecord(reader, [])
                : null;
        }

        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (record.Status != "Approved")
        {
            throw new GovernanceExceptionConflictException("Only an approved exception can be revoked.");
        }

        await using (var updateCommand = new SqlCommand(
            """
            UPDATE dbo.BiAgentGovernanceExceptions
            SET Status = N'Revoked'
            WHERE ExceptionId = @ExceptionId AND Status = N'Approved';
            """,
            connection,
            transaction))
        {
            updateCommand.Parameters.Add("@ExceptionId", SqlDbType.UniqueIdentifier).Value = exceptionId;
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertAuditEventAsync(
            connection,
            transaction,
            exceptionId,
            "Revoked",
            revokedUtc,
            revokedBy,
            reason,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return record with
        {
            Status = "Revoked",
            AuditTrail = record.AuditTrail.Append(
                new GovernanceExceptionAuditEvent("Revoked", revokedUtc, revokedBy, reason)).ToArray()
        };
    }

    public async Task<IReadOnlyList<GovernanceExceptionRecord>> GetForFindingsAsync(
        IReadOnlyCollection<GovernanceFinding> findings,
        CancellationToken cancellationToken)
    {
        var workspaceIds = findings.Select(finding => finding.WorkspaceId).Distinct().ToArray();
        if (workspaceIds.Length == 0)
        {
            return [];
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var exceptions = new Dictionary<Guid, GovernanceExceptionRecord>();
        await using (var command = new SqlCommand(
            """
            SELECT ExceptionId, WorkspaceId, CheckId, ArtifactId, ArtifactType, ArtifactName, Owner, Reason,
                   ApprovedBy, ApprovedUtc, ExpiresUtc, Status, SourceRunId
            FROM dbo.BiAgentGovernanceExceptions
            WHERE WorkspaceId IN
                (SELECT TRY_CONVERT(uniqueidentifier, [value]) FROM OPENJSON(@WorkspaceIds));
            """,
            connection))
        {
            command.Parameters.Add("@WorkspaceIds", SqlDbType.NVarChar, -1).Value =
                JsonSerializer.Serialize(workspaceIds);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var record = ReadRecord(reader, []);
                exceptions.Add(record.ExceptionId, record);
            }
        }

        if (exceptions.Count == 0)
        {
            return [];
        }

        var auditEvents = new Dictionary<Guid, List<GovernanceExceptionAuditEvent>>();
        await using (var command = new SqlCommand(
            """
            SELECT ExceptionId, EventType, EventUtc, Actor, Notes
            FROM dbo.BiAgentGovernanceExceptionAudit
            WHERE ExceptionId IN
                (SELECT TRY_CONVERT(uniqueidentifier, [value]) FROM OPENJSON(@ExceptionIds))
            ORDER BY EventUtc, AuditEventId;
            """,
            connection))
        {
            command.Parameters.Add("@ExceptionIds", SqlDbType.NVarChar, -1).Value =
                JsonSerializer.Serialize(exceptions.Keys);
            foreach (var exceptionId in exceptions.Keys)
            {
                auditEvents[exceptionId] = [];
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                auditEvents[reader.GetGuid(0)].Add(new GovernanceExceptionAuditEvent(
                    reader.GetString(1),
                    reader.GetDateTimeOffset(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
        }

        return exceptions.Values.Select(record => record with
        {
            AuditTrail = auditEvents[record.ExceptionId]
        }).ToArray();
    }

    private static void AddFindingKeyParameters(
        SqlCommand command,
        CreateGovernanceExceptionRequest request)
    {
        command.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = request.WorkspaceId;
        command.Parameters.Add("@CheckId", SqlDbType.NVarChar, 40).Value = request.CheckId;
        command.Parameters.Add("@ArtifactId", SqlDbType.UniqueIdentifier).Value =
            (object?)request.ArtifactId ?? DBNull.Value;
        command.Parameters.Add("@ArtifactType", SqlDbType.NVarChar, 40).Value = request.ArtifactType ?? string.Empty;
        command.Parameters.Add("@ArtifactName", SqlDbType.NVarChar, 256).Value = request.ArtifactName;
    }

    private static async Task InsertAuditEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid exceptionId,
        string eventType,
        DateTimeOffset eventUtc,
        string actor,
        string notes,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentGovernanceExceptionAudit
                (ExceptionId, EventType, EventUtc, Actor, Notes)
            VALUES
                (@ExceptionId, @EventType, @EventUtc, @Actor, @Notes);
            """,
            connection,
            transaction);
        command.Parameters.Add("@ExceptionId", SqlDbType.UniqueIdentifier).Value = exceptionId;
        command.Parameters.Add("@EventType", SqlDbType.NVarChar, 20).Value = eventType;
        command.Parameters.Add("@EventUtc", SqlDbType.DateTimeOffset).Value = eventUtc;
        command.Parameters.Add("@Actor", SqlDbType.NVarChar, 256).Value = actor;
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 2000).Value = notes;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static GovernanceExceptionRecord ReadRecord(
        SqlDataReader reader,
        IReadOnlyList<GovernanceExceptionAuditEvent> auditTrail) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetDateTimeOffset(9),
            reader.GetDateTimeOffset(10),
            reader.GetString(11),
            reader.GetGuid(12),
            auditTrail);
}

public sealed class GovernanceExceptionConflictException(string message) : Exception(message);
