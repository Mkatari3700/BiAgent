using System.Data;
using BISentinel.Api.GovernanceAudit;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.GovernanceAudit;

public sealed class SqlGovernanceAuditHistoryStore(string connectionString) : IGovernanceAuditHistoryStore
{
    public async Task<Guid> SaveAsync(
        GovernanceAuditResult result,
        string scannedBy,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await using (var command = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentGovernanceAuditRuns (RunId, ScannedUtc, ScannedBy)
            VALUES (@RunId, @ScannedUtc, @ScannedBy);
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            command.Parameters.Add("@ScannedUtc", SqlDbType.DateTime2).Value = result.ScannedUtc.UtcDateTime;
            command.Parameters.Add("@ScannedBy", SqlDbType.NVarChar, 256).Value = scannedBy;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var audit in result.Workspaces)
        {
            await using (var command = new SqlCommand(
                """
                INSERT INTO dbo.BiAgentGovernanceAuditRunWorkspaces
                    (RunId, WorkspaceId, WorkspaceName, IsReadOnly, IsOnDedicatedCapacity)
                VALUES
                    (@RunId, @WorkspaceId, @WorkspaceName, @IsReadOnly, @IsOnDedicatedCapacity);
                """,
                connection,
                transaction))
            {
                command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
                command.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = audit.Workspace.Id;
                command.Parameters.Add("@WorkspaceName", SqlDbType.NVarChar, 256).Value = audit.Workspace.Name;
                command.Parameters.Add("@IsReadOnly", SqlDbType.Bit).Value = audit.Workspace.IsReadOnly;
                command.Parameters.Add("@IsOnDedicatedCapacity", SqlDbType.Bit).Value =
                    audit.Workspace.IsOnDedicatedCapacity;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var user in audit.Users)
            {
                await using var command = new SqlCommand(
                    """
                    INSERT INTO dbo.BiAgentGovernanceAuditRunUsers
                        (RunId, WorkspaceId, Identifier, DisplayName, EmailAddress, PrincipalType, AccessRight)
                    VALUES
                        (@RunId, @WorkspaceId, @Identifier, @DisplayName, @EmailAddress, @PrincipalType, @AccessRight);
                    """,
                    connection,
                    transaction);
                command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
                command.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = audit.Workspace.Id;
                command.Parameters.Add("@Identifier", SqlDbType.NVarChar, 256).Value = user.Identifier;
                command.Parameters.Add("@DisplayName", SqlDbType.NVarChar, 256).Value =
                    (object?)user.DisplayName ?? DBNull.Value;
                command.Parameters.Add("@EmailAddress", SqlDbType.NVarChar, 256).Value =
                    (object?)user.EmailAddress ?? DBNull.Value;
                command.Parameters.Add("@PrincipalType", SqlDbType.NVarChar, 40).Value = user.PrincipalType;
                command.Parameters.Add("@AccessRight", SqlDbType.NVarChar, 40).Value = user.AccessRight;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var artifact in audit.Reports.Concat(audit.SemanticModels))
            {
                await using var command = new SqlCommand(
                    """
                    INSERT INTO dbo.BiAgentGovernanceAuditRunArtifacts
                        (RunId, WorkspaceId, ArtifactId, ArtifactName, ArtifactType, DatasetId, ConfiguredBy, WebUrl)
                    VALUES
                        (@RunId, @WorkspaceId, @ArtifactId, @ArtifactName, @ArtifactType, @DatasetId, @ConfiguredBy, @WebUrl);
                    """,
                    connection,
                    transaction);
                command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
                command.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = audit.Workspace.Id;
                command.Parameters.Add("@ArtifactId", SqlDbType.UniqueIdentifier).Value = artifact.Id;
                command.Parameters.Add("@ArtifactName", SqlDbType.NVarChar, 256).Value = artifact.Name;
                command.Parameters.Add("@ArtifactType", SqlDbType.NVarChar, 40).Value = artifact.Type;
                command.Parameters.Add("@DatasetId", SqlDbType.UniqueIdentifier).Value =
                    Guid.TryParse(artifact.DatasetId, out var datasetId) ? datasetId : DBNull.Value;
                command.Parameters.Add("@ConfiguredBy", SqlDbType.NVarChar, 256).Value =
                    (object?)artifact.ConfiguredBy ?? DBNull.Value;
                command.Parameters.Add("@WebUrl", SqlDbType.NVarChar, 2048).Value =
                    (object?)artifact.WebUrl ?? DBNull.Value;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var finding in result.Findings)
        {
            await using var command = new SqlCommand(
                """
                INSERT INTO dbo.BiAgentGovernanceAuditRunFindings
                    (RunId, Code, Severity, WorkspaceId, WorkspaceName, ArtifactId, Title, Detail)
                VALUES
                    (@RunId, @Code, @Severity, @WorkspaceId, @WorkspaceName, @ArtifactId, @Title, @Detail);
                """,
                connection,
                transaction);
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            command.Parameters.Add("@Code", SqlDbType.NVarChar, 40).Value = finding.Code;
            command.Parameters.Add("@Severity", SqlDbType.NVarChar, 20).Value = finding.Severity;
            command.Parameters.Add("@WorkspaceId", SqlDbType.UniqueIdentifier).Value = finding.WorkspaceId;
            command.Parameters.Add("@WorkspaceName", SqlDbType.NVarChar, 256).Value = finding.WorkspaceName;
            command.Parameters.Add("@ArtifactId", SqlDbType.UniqueIdentifier).Value =
                (object?)finding.ArtifactId ?? DBNull.Value;
            command.Parameters.Add("@Title", SqlDbType.NVarChar, 256).Value = finding.Title;
            command.Parameters.Add("@Detail", SqlDbType.NVarChar, 2000).Value = finding.Detail;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return runId;
    }

    public async Task<IReadOnlyList<GovernanceAuditRunSummary>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT TOP (@Limit)
                run.RunId,
                run.ScannedUtc,
                run.ScannedBy,
                (SELECT COUNT(*) FROM dbo.BiAgentGovernanceAuditRunWorkspaces AS workspace
                 WHERE workspace.RunId = run.RunId) AS WorkspaceCount,
                (SELECT COUNT(*) FROM dbo.BiAgentGovernanceAuditRunArtifacts AS artifact
                 WHERE artifact.RunId = run.RunId AND artifact.ArtifactType = N'Report') AS ReportCount,
                (SELECT COUNT(*) FROM dbo.BiAgentGovernanceAuditRunArtifacts AS artifact
                 WHERE artifact.RunId = run.RunId AND artifact.ArtifactType = N'Semantic model') AS SemanticModelCount,
                (SELECT COUNT(*) FROM dbo.BiAgentGovernanceAuditRunFindings AS finding
                 WHERE finding.RunId = run.RunId) AS FindingCount
            FROM dbo.BiAgentGovernanceAuditRuns AS run
            ORDER BY run.ScannedUtc DESC;
            """,
            connection);
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        var summaries = new List<GovernanceAuditRunSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new GovernanceAuditRunSummary(
                reader.GetGuid(0),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        return summaries;
    }

    public async Task<GovernanceAuditResult?> GetAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        DateTime scannedUtc;
        await using (var command = new SqlCommand(
            "SELECT ScannedUtc FROM dbo.BiAgentGovernanceAuditRuns WHERE RunId = @RunId;",
            connection))
        {
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is not DateTime timestamp)
            {
                return null;
            }

            scannedUtc = timestamp;
        }

        var workspaces = new Dictionary<Guid, GovernanceWorkspaceAuditBuilder>();
        await using (var command = new SqlCommand(
            """
            SELECT WorkspaceId, WorkspaceName, IsReadOnly, IsOnDedicatedCapacity
            FROM dbo.BiAgentGovernanceAuditRunWorkspaces
            WHERE RunId = @RunId;
            """,
            connection))
        {
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var workspace = new GovernanceWorkspace(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    reader.GetBoolean(3));
                workspaces.Add(workspace.Id, new GovernanceWorkspaceAuditBuilder(workspace));
            }
        }

        await using (var command = new SqlCommand(
            """
            SELECT WorkspaceId, Identifier, DisplayName, EmailAddress, PrincipalType, AccessRight
            FROM dbo.BiAgentGovernanceAuditRunUsers
            WHERE RunId = @RunId;
            """,
            connection))
        {
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                workspaces[reader.GetGuid(0)].Users.Add(new GovernanceWorkspaceUser(
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
        }

        await using (var command = new SqlCommand(
            """
            SELECT WorkspaceId, ArtifactId, ArtifactName, ArtifactType, DatasetId, ConfiguredBy, WebUrl
            FROM dbo.BiAgentGovernanceAuditRunArtifacts
            WHERE RunId = @RunId;
            """,
            connection))
        {
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var artifact = new GovernanceArtifact(
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4).ToString(),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6));
                var workspace = workspaces[reader.GetGuid(0)];
                if (artifact.Type == "Report")
                {
                    workspace.Reports.Add(artifact);
                }
                else
                {
                    workspace.SemanticModels.Add(artifact);
                }
            }
        }

        var findings = new List<GovernanceFinding>();
        await using (var command = new SqlCommand(
            """
            SELECT Code, Severity, WorkspaceId, WorkspaceName, ArtifactId, Title, Detail
            FROM dbo.BiAgentGovernanceAuditRunFindings
            WHERE RunId = @RunId
            ORDER BY FindingId;
            """,
            connection))
        {
            command.Parameters.Add("@RunId", SqlDbType.UniqueIdentifier).Value = runId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                findings.Add(new GovernanceFinding(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        return new GovernanceAuditResult(
            runId,
            new DateTimeOffset(DateTime.SpecifyKind(scannedUtc, DateTimeKind.Utc)),
            workspaces.Values.Select(workspace => workspace.Build()).ToArray(),
            findings);
    }

    private sealed class GovernanceWorkspaceAuditBuilder(GovernanceWorkspace workspace)
    {
        public List<GovernanceWorkspaceUser> Users { get; } = [];

        public List<GovernanceArtifact> Reports { get; } = [];

        public List<GovernanceArtifact> SemanticModels { get; } = [];

        public GovernanceWorkspaceAudit Build() =>
            new(workspace, Users, Reports, SemanticModels);
    }
}
