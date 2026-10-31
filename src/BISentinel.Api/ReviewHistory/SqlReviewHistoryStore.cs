using System.Data;
using Microsoft.Data.SqlClient;
using BISentinel.Api.Reviews;

namespace BISentinel.Api.ReviewHistory;

public sealed class SqlReviewHistoryStore(string connectionString) : IReviewHistoryStore
{
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.BiAgentReviewRuns WHERE ExpiresUtc <= SYSUTCDATETIME();";
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReviewHistoryEntry> SaveAsync(
        PbipReviewResult review,
        int retentionDays,
        Guid uploadedByUserId,
        CancellationToken cancellationToken)
    {
        var projectName = ReviewProjectName.GetBaseName(review.ProjectName);
        var savedReview = review with { ProjectName = projectName };
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = "DELETE FROM dbo.BiAgentReviewRuns WHERE ExpiresUtc <= SYSUTCDATETIME();";
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        var candidateRuns = new List<(Guid Id, string Name, DateTime ReviewedUtc)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT ReviewRunId, ProjectName, ReviewedUtc
                FROM dbo.BiAgentReviewRuns WITH (UPDLOCK, HOLDLOCK)
                WHERE UploadedByUserId = @UploadedByUserId
                  AND (ProjectName = @ProjectName
                   OR ProjectName LIKE @VersionedNamePattern ESCAPE '~');
                """;
            command.Parameters.Add("@UploadedByUserId", SqlDbType.UniqueIdentifier).Value = uploadedByUserId;
            command.Parameters.Add("@ProjectName", SqlDbType.NVarChar, 400).Value = projectName;
            command.Parameters.Add("@VersionedNamePattern", SqlDbType.NVarChar, 410).Value =
                EscapeLikePattern(projectName) + " (%";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var existingName = reader.GetString(1);
                if (ReviewProjectName.IsSameProject(existingName, projectName))
                {
                    candidateRuns.Add((
                        reader.GetGuid(0),
                        existingName,
                        DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
                }
            }
        }

        var reviewRunId = candidateRuns
            .OrderByDescending(run => run.ReviewedUtc)
            .Select(run => (Guid?)run.Id)
            .FirstOrDefault() ?? Guid.NewGuid();
        var candidateRunIds = candidateRuns.Select(run => run.Id).ToHashSet();
        var previousFindings = new List<(Guid RunId, Guid FindingId, byte[] Fingerprint, DateTime ReviewedUtc)>();
        if (candidateRunIds.Count > 0)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE #ProjectRuns (ReviewRunId uniqueidentifier NOT NULL PRIMARY KEY);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (var run in candidateRuns)
            {
                await using var insertRun = connection.CreateCommand();
                insertRun.Transaction = transaction;
                insertRun.CommandText = "INSERT INTO #ProjectRuns (ReviewRunId) VALUES (@ReviewRunId);";
                insertRun.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = run.Id;
                await insertRun.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var findingsCommand = connection.CreateCommand();
            findingsCommand.Transaction = transaction;
            findingsCommand.CommandText = """
                SELECT finding.ReviewRunId, finding.ReviewFindingId, finding.FindingFingerprint, run.ReviewedUtc
                FROM dbo.BiAgentReviewFindings AS finding
                INNER JOIN #ProjectRuns AS projectRun ON projectRun.ReviewRunId = finding.ReviewRunId
                INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = finding.ReviewRunId;
                """;
            await using var findingsReader = await findingsCommand.ExecuteReaderAsync(cancellationToken);
            while (await findingsReader.ReadAsync(cancellationToken))
            {
                previousFindings.Add((
                    findingsReader.GetGuid(0),
                    findingsReader.GetGuid(1),
                    findingsReader.GetFieldValue<byte[]>(2),
                    DateTime.SpecifyKind(findingsReader.GetDateTime(3), DateTimeKind.Utc)));
            }
        }

        var existingRunOrder = candidateRuns.ToDictionary(run => run.Id, run => run.ReviewedUtc);
        var previousByFingerprint = previousFindings
            .GroupBy(finding => Convert.ToHexString(finding.Fingerprint), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(finding => finding.RunId == reviewRunId)
                    .ThenByDescending(finding => existingRunOrder[finding.RunId])
                    .Select(finding => finding.FindingId)
                    .ToList(),
                StringComparer.Ordinal);
        var usedFindingIds = new HashSet<Guid>();
        var preparedFindings = savedReview.Findings
            .Select((finding, index) =>
            {
                var fingerprint = ReviewFindingIdentity.CreateFingerprint(
                    savedReview.ProjectId,
                    finding.RuleId,
                    finding.Location);
                var fingerprintKey = Convert.ToHexString(fingerprint);
                var existingFindingId = previousByFingerprint.TryGetValue(fingerprintKey, out var ids)
                    ? ids.FirstOrDefault(id => usedFindingIds.Add(id))
                    : Guid.Empty;
                var findingId = existingFindingId == Guid.Empty ? Guid.NewGuid() : existingFindingId;
                return (Finding: finding, Index: index, Id: findingId, Fingerprint: fingerprint, FingerprintKey: fingerprintKey);
            })
            .ToList();

        if (candidateRunIds.Count > 0)
        {
            await using var stashFeedback = connection.CreateCommand();
            stashFeedback.Transaction = transaction;
            stashFeedback.CommandText = """
                CREATE TABLE #PreviousFeedback
                (
                    ReviewerId uniqueidentifier NOT NULL,
                    FindingFingerprint binary(32) NOT NULL,
                    Rating nvarchar(20) NULL,
                    Comment nvarchar(500) NULL,
                    FeedbackStatus nvarchar(10) NOT NULL,
                    UpdatedUtc datetime2(3) NOT NULL,
                    FeedbackId bigint NOT NULL
                );

                WITH RankedFeedback AS
                (
                    SELECT
                        feedback.ReviewerId,
                        finding.FindingFingerprint,
                        feedback.Rating,
                        feedback.Comment,
                        feedback.FeedbackStatus,
                        feedback.UpdatedUtc,
                        feedback.FeedbackId,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY feedback.ReviewerId, finding.FindingFingerprint
                            ORDER BY feedback.UpdatedUtc DESC, feedback.FeedbackId DESC
                        ) AS FeedbackRank
                    FROM dbo.BiAgentReviewFeedback AS feedback
                    INNER JOIN dbo.BiAgentReviewFindings AS finding
                        ON finding.ReviewFindingId = feedback.ReviewFindingId
                    INNER JOIN #ProjectRuns AS projectRun
                        ON projectRun.ReviewRunId = finding.ReviewRunId
                )
                INSERT INTO #PreviousFeedback
                    (ReviewerId, FindingFingerprint, Rating, Comment, FeedbackStatus, UpdatedUtc, FeedbackId)
                SELECT
                    ReviewerId, FindingFingerprint, Rating, Comment, FeedbackStatus, UpdatedUtc, FeedbackId
                FROM RankedFeedback
                WHERE FeedbackRank = 1;
                """;
            await stashFeedback.ExecuteNonQueryAsync(cancellationToken);
        }

        if (candidateRunIds.Count > 0)
        {
            await using var deleteRuns = connection.CreateCommand();
            deleteRuns.Transaction = transaction;
            deleteRuns.CommandText = """
                DELETE run
                FROM dbo.BiAgentReviewRuns AS run
                INNER JOIN #ProjectRuns AS projectRun ON projectRun.ReviewRunId = run.ReviewRunId;
                """;
            await deleteRuns.ExecuteNonQueryAsync(cancellationToken);
        }

        DateTime reviewedUtc;
        DateTime expiresUtc;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO dbo.BiAgentReviewRuns
                    (ReviewRunId, UploadedByUserId, ProjectId, ProjectName, TablesChecked, MeasuresChecked, RelationshipsChecked,
                     ChecksRun, FindingsCount, ReviewedUtc, ExpiresUtc)
                VALUES
                    (@ReviewRunId, @UploadedByUserId, @ProjectId, @ProjectName, @TablesChecked, @MeasuresChecked, @RelationshipsChecked,
                     @ChecksRun, @FindingsCount, SYSUTCDATETIME(),
                     DATEADD(day, @RetentionDays, SYSUTCDATETIME()));

                SELECT ReviewedUtc, ExpiresUtc
                FROM dbo.BiAgentReviewRuns
                WHERE ReviewRunId = @ReviewRunId;
                """;
            command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
            command.Parameters.Add("@UploadedByUserId", SqlDbType.UniqueIdentifier).Value = uploadedByUserId;
            command.Parameters.Add("@ProjectId", SqlDbType.NVarChar, 100).Value = review.ProjectId;
            command.Parameters.Add("@ProjectName", SqlDbType.NVarChar, 400).Value = savedReview.ProjectName;
            command.Parameters.Add("@TablesChecked", SqlDbType.Int).Value = savedReview.TablesChecked;
            command.Parameters.Add("@MeasuresChecked", SqlDbType.Int).Value = savedReview.MeasuresChecked;
            command.Parameters.Add("@RelationshipsChecked", SqlDbType.Int).Value = savedReview.RelationshipsChecked;
            command.Parameters.Add("@ChecksRun", SqlDbType.Int).Value = savedReview.ChecksRun;
            command.Parameters.Add("@FindingsCount", SqlDbType.Int).Value = savedReview.Findings.Count;
            command.Parameters.Add("@RetentionDays", SqlDbType.Int).Value = retentionDays;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Review history save did not return its timestamps.");
            }

            reviewedUtc = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            expiresUtc = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
        }

        var savedFindings = new List<ReviewHistoryFinding>(savedReview.Findings.Count);
        foreach (var prepared in preparedFindings)
        {
            var finding = prepared.Finding;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO dbo.BiAgentReviewFindings
                    (ReviewRunId, ReviewFindingId, FindingFingerprint, FindingOrdinal, RuleId, Severity,
                     Message, Location, Recommendation)
                VALUES
                    (@ReviewRunId, @ReviewFindingId, @FindingFingerprint, @FindingOrdinal, @RuleId, @Severity,
                     @Message, @Location, @Recommendation);
                """;
            command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
            command.Parameters.Add("@ReviewFindingId", SqlDbType.UniqueIdentifier).Value = prepared.Id;
            command.Parameters.Add("@FindingFingerprint", SqlDbType.Binary, 32).Value = prepared.Fingerprint;
            command.Parameters.Add("@FindingOrdinal", SqlDbType.Int).Value = prepared.Index;
            command.Parameters.Add("@RuleId", SqlDbType.NVarChar, 100).Value = finding.RuleId;
            command.Parameters.Add("@Severity", SqlDbType.NVarChar, 20).Value = finding.Severity;
            command.Parameters.Add("@Message", SqlDbType.NVarChar, -1).Value = finding.Message;
            command.Parameters.Add("@Location", SqlDbType.NVarChar, 1000).Value = finding.Location;
            command.Parameters.Add("@Recommendation", SqlDbType.NVarChar, -1).Value = finding.Recommendation;
            await command.ExecuteNonQueryAsync(cancellationToken);
            savedFindings.Add(new ReviewHistoryFinding(
                prepared.Id,
                prepared.FingerprintKey,
                finding.RuleId,
                finding.Severity,
                finding.Message,
                finding.Location,
                finding.Recommendation));
        }

        if (candidateRunIds.Count > 0)
        {
            await using var restoreFeedback = connection.CreateCommand();
            restoreFeedback.Transaction = transaction;
            restoreFeedback.CommandText = """
                WITH RankedCurrentFindingFingerprints AS
                (
                    SELECT
                        FindingFingerprint,
                        ReviewFindingId,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY FindingFingerprint
                            ORDER BY FindingOrdinal
                        ) AS FindingRank
                    FROM dbo.BiAgentReviewFindings
                    WHERE ReviewRunId = @ReviewRunId
                )
                INSERT INTO dbo.BiAgentReviewFeedback
                    (ReviewerId, ReviewFindingId, ProjectName, FindingKey, RuleId, Severity, Message, Location,
                     Rating, Comment, FeedbackStatus, UpdatedUtc)
                SELECT
                    previous.ReviewerId,
                    currentFinding.ReviewFindingId,
                    @ProjectName,
                    currentFinding.FindingFingerprint,
                    finding.RuleId,
                    finding.Severity,
                    finding.Message,
                    finding.Location,
                    previous.Rating,
                    previous.Comment,
                    previous.FeedbackStatus,
                    previous.UpdatedUtc
                FROM #PreviousFeedback AS previous
                INNER JOIN RankedCurrentFindingFingerprints AS currentFinding
                    ON currentFinding.FindingFingerprint = previous.FindingFingerprint
                   AND currentFinding.FindingRank = 1
                INNER JOIN dbo.BiAgentReviewFindings AS finding
                    ON finding.ReviewFindingId = currentFinding.ReviewFindingId;
                """;
            restoreFeedback.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
            restoreFeedback.Parameters.Add("@ProjectName", SqlDbType.NVarChar, 400).Value = projectName;
            await restoreFeedback.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ReviewHistoryEntry(
            new ReviewHistorySummary(
                reviewRunId,
                savedReview.ProjectId,
                savedReview.ProjectName,
                reviewedUtc,
                expiresUtc,
                savedReview.TablesChecked,
                savedReview.MeasuresChecked,
                savedReview.RelationshipsChecked,
                savedReview.ChecksRun,
                savedReview.Findings.Count),
            savedFindings);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);

    public async Task<IReadOnlyList<ReviewHistorySummary>> GetRecentAsync(
        int limit,
        string? projectName,
        Guid? uploadedByUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@Limit)
                ProjectId,
                ReviewRunId,
                ProjectName,
                ReviewedUtc,
                ExpiresUtc,
                TablesChecked,
                MeasuresChecked,
                RelationshipsChecked,
                ChecksRun,
                FindingsCount
            FROM dbo.BiAgentReviewRuns
            WHERE ExpiresUtc > SYSUTCDATETIME()
              AND (@UploadedByUserId IS NULL OR UploadedByUserId = @UploadedByUserId)
              AND (@ProjectName IS NULL OR ProjectName LIKE @ProjectName ESCAPE '~')
            ORDER BY ReviewedUtc DESC;
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        command.Parameters.Add("@UploadedByUserId", SqlDbType.UniqueIdentifier).Value =
            uploadedByUserId is null ? DBNull.Value : uploadedByUserId.Value;
        var escapedProjectName = projectName?
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);
        command.Parameters.Add("@ProjectName", SqlDbType.NVarChar, 802).Value =
            escapedProjectName is null ? DBNull.Value : $"%{escapedProjectName}%";

        var summaries = new List<ReviewHistorySummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(ReadSummary(reader));
        }

        return summaries;
    }

    public async Task<ReviewHistoryEntry?> GetAsync(
        Guid reviewRunId,
        Guid? uploadedByUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        ReviewHistorySummary summary;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    r.ProjectId,
                    r.ReviewRunId,
                    r.ProjectName,
                    r.ReviewedUtc,
                    r.ExpiresUtc,
                    r.TablesChecked,
                    r.MeasuresChecked,
                    r.RelationshipsChecked,
                    r.ChecksRun,
                    r.FindingsCount
                FROM dbo.BiAgentReviewRuns r
                WHERE ReviewRunId = @ReviewRunId
                  AND (@UploadedByUserId IS NULL OR UploadedByUserId = @UploadedByUserId)
                  AND ExpiresUtc > SYSUTCDATETIME();
                """;
            command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
            command.Parameters.Add("@UploadedByUserId", SqlDbType.UniqueIdentifier).Value =
                uploadedByUserId is null ? DBNull.Value : uploadedByUserId.Value;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            summary = ReadSummary(reader);
        }

        var findings = new List<ReviewHistoryFinding>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ReviewFindingId, FindingFingerprint, RuleId, Severity, Message, Location, Recommendation
                FROM dbo.BiAgentReviewFindings
                WHERE ReviewRunId = @ReviewRunId
                ORDER BY FindingOrdinal;
                """;
            command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                findings.Add(new ReviewHistoryFinding(
                    reader.GetGuid(0),
                    Convert.ToHexString(reader.GetFieldValue<byte[]>(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        return new ReviewHistoryEntry(summary, findings);
    }

    private static ReviewHistorySummary ReadSummary(SqlDataReader reader) =>
        new(
            reader.GetGuid(1),
            reader.GetString(0),
            reader.GetString(2),
            DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9));

}
