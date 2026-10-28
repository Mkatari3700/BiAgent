using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Feedback;

public sealed class SqlReviewFeedbackStore(string connectionString) : IReviewFeedbackStore
{
    public async Task<DateTime?> SaveAsync(
        SaveReviewFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @FindingProjectName nvarchar(400);
            DECLARE @FindingKey binary(32);
            DECLARE @FindingRuleId nvarchar(100);
            DECLARE @FindingSeverity nvarchar(20);
            DECLARE @FindingMessage nvarchar(max);
            DECLARE @FindingLocation nvarchar(1000);

            SELECT
                @FindingProjectName = run.ProjectName,
                @FindingKey = finding.FindingFingerprint,
                @FindingRuleId = finding.RuleId,
                @FindingSeverity = finding.Severity,
                @FindingMessage = finding.Message,
                @FindingLocation = finding.Location
            FROM dbo.BiAgentReviewFindings AS finding
            INNER JOIN dbo.BiAgentReviewRuns AS run
                ON run.ReviewRunId = finding.ReviewRunId
            WHERE finding.ReviewFindingId = @ReviewFindingId
              AND run.ExpiresUtc > SYSUTCDATETIME();

            IF @FindingKey IS NULL
            BEGIN
                ROLLBACK TRANSACTION;
                RETURN;
            END;

            UPDATE dbo.BiAgentReviewFeedback WITH (UPDLOCK, HOLDLOCK)
            SET Severity = @FindingSeverity,
                Message = @FindingMessage,
                Rating = @Rating,
                Comment = @Comment,
                FeedbackStatus = N'Closed',
                UpdatedUtc = SYSUTCDATETIME()
            WHERE ReviewerId = @ReviewerId
              AND ReviewFindingId = @ReviewFindingId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.BiAgentReviewFeedback
                    (ReviewerId, ReviewFindingId, ProjectName, FindingKey, RuleId, Severity, Message, Location,
                     Rating, Comment, FeedbackStatus)
                VALUES
                    (@ReviewerId, @ReviewFindingId, @FindingProjectName, @FindingKey, @FindingRuleId,
                     @FindingSeverity, @FindingMessage, @FindingLocation, @Rating, @Comment, N'Closed');
            END;

            SELECT UpdatedUtc
            FROM dbo.BiAgentReviewFeedback
            WHERE ReviewerId = @ReviewerId
              AND ReviewFindingId = @ReviewFindingId;
            """;
        command.Parameters.Add("@ReviewerId", SqlDbType.UniqueIdentifier).Value = request.ReviewerId;
        command.Parameters.Add("@ReviewFindingId", SqlDbType.UniqueIdentifier).Value = request.ReviewFindingId;
        command.Parameters.Add("@Rating", SqlDbType.NVarChar, 20).Value =
            string.IsNullOrWhiteSpace(request.Rating) ? DBNull.Value : request.Rating;
        command.Parameters.Add("@Comment", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();
        var updatedUtc = (DateTime?)await command.ExecuteScalarAsync(cancellationToken);
        if (updatedUtc is null)
        {
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return DateTime.SpecifyKind(updatedUtc.Value, DateTimeKind.Utc);
    }

    public async Task<DateTime?> ReopenAsync(
        Guid reviewerId,
        Guid reviewFindingId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE feedback
            SET FeedbackStatus = N'Open',
                UpdatedUtc = SYSUTCDATETIME()
            OUTPUT inserted.UpdatedUtc
            FROM dbo.BiAgentReviewFeedback AS feedback
            INNER JOIN dbo.BiAgentReviewFindings AS finding
                ON finding.ReviewFindingId = feedback.ReviewFindingId
            INNER JOIN dbo.BiAgentReviewRuns AS run
                ON run.ReviewRunId = finding.ReviewRunId
            WHERE feedback.ReviewerId = @ReviewerId
              AND feedback.ReviewFindingId = @ReviewFindingId
              AND run.ExpiresUtc > SYSUTCDATETIME();
            """;
        command.Parameters.Add("@ReviewerId", SqlDbType.UniqueIdentifier).Value = reviewerId;
        command.Parameters.Add("@ReviewFindingId", SqlDbType.UniqueIdentifier).Value = reviewFindingId;
        var updatedUtc = (DateTime?)await command.ExecuteScalarAsync(cancellationToken);
        return updatedUtc is null ? null : DateTime.SpecifyKind(updatedUtc.Value, DateTimeKind.Utc);
    }

    public async Task<DateTime?> ReopenRunAsync(
        Guid reviewerId,
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE feedback
            SET FeedbackStatus = N'Open',
                UpdatedUtc = SYSUTCDATETIME()
            OUTPUT inserted.UpdatedUtc
            FROM dbo.BiAgentReviewFeedback AS feedback
            INNER JOIN dbo.BiAgentReviewFindings AS finding
                ON finding.ReviewFindingId = feedback.ReviewFindingId
            INNER JOIN dbo.BiAgentReviewRuns AS run
                ON run.ReviewRunId = finding.ReviewRunId
            WHERE feedback.ReviewerId = @ReviewerId
              AND run.ReviewRunId = @ReviewRunId
              AND run.ExpiresUtc > SYSUTCDATETIME()
              AND feedback.FeedbackStatus = N'Closed';
            """;
        command.Parameters.Add("@ReviewerId", SqlDbType.UniqueIdentifier).Value = reviewerId;
        command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
        var updatedUtc = (DateTime?)await command.ExecuteScalarAsync(cancellationToken);
        if (updatedUtc is not null)
        {
            return DateTime.SpecifyKind(updatedUtc.Value, DateTimeKind.Utc);
        }

        await using var exists = connection.CreateCommand();
        exists.CommandText = """
            SELECT ReviewedUtc
            FROM dbo.BiAgentReviewRuns
            WHERE ReviewRunId = @ReviewRunId
              AND ExpiresUtc > SYSUTCDATETIME();
            """;
        exists.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
        var reviewedUtc = (DateTime?)await exists.ExecuteScalarAsync(cancellationToken);
        return reviewedUtc is null ? null : DateTime.SpecifyKind(reviewedUtc.Value, DateTimeKind.Utc);
    }

    public async Task<IReadOnlyList<ReviewFeedbackEntry>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@Limit)
                feedback.ReviewerId,
                feedback.ReviewFindingId,
                CONVERT(varchar(64), finding.FindingFingerprint, 2),
                feedback.FeedbackStatus,
                feedback.ProjectName,
                feedback.RuleId,
                feedback.Severity,
                feedback.Message,
                feedback.Location,
                feedback.Rating,
                feedback.Comment,
                feedback.UpdatedUtc
            FROM dbo.BiAgentReviewFeedback AS feedback
            LEFT JOIN dbo.BiAgentReviewFindings AS finding
                ON finding.ReviewFindingId = feedback.ReviewFindingId
            LEFT JOIN dbo.BiAgentReviewRuns AS run
                ON run.ReviewRunId = finding.ReviewRunId
            WHERE feedback.ReviewFindingId IS NULL
               OR run.ExpiresUtc > SYSUTCDATETIME()
            ORDER BY feedback.UpdatedUtc DESC;
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;

        var entries = new List<ReviewFeedbackEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new ReviewFeedbackEntry(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc)));
        }

        return entries;
    }

    public async Task<IReadOnlyList<ReviewDashboardRun>> GetDashboardAsync(
        Guid reviewerId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@Limit)
                run.ReviewRunId,
                run.ProjectName,
                run.ReviewedUtc,
                COUNT(finding.ReviewFindingId),
                SUM(CASE WHEN feedback.FeedbackStatus = N'Closed' THEN 1 ELSE 0 END),
                SUM(CASE WHEN finding.ReviewFindingId IS NOT NULL
                              AND (feedback.FeedbackStatus IS NULL OR feedback.FeedbackStatus = N'Open')
                         THEN 1 ELSE 0 END),
                CASE
                    WHEN COUNT(finding.ReviewFindingId) = 0
                      OR SUM(CASE WHEN feedback.FeedbackStatus = N'Closed' THEN 1 ELSE 0 END) =
                         COUNT(finding.ReviewFindingId)
                    THEN N'Closed'
                    ELSE N'Open'
                END
            FROM dbo.BiAgentReviewRuns AS run
            LEFT JOIN dbo.BiAgentReviewFindings AS finding
                ON finding.ReviewRunId = run.ReviewRunId
            LEFT JOIN dbo.BiAgentReviewFeedback AS feedback
                ON feedback.ReviewFindingId = finding.ReviewFindingId
               AND feedback.ReviewerId = @ReviewerId
            WHERE run.ExpiresUtc > SYSUTCDATETIME()
            GROUP BY run.ReviewRunId, run.ProjectName, run.ReviewedUtc
            ORDER BY run.ReviewedUtc DESC;
            """;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
        command.Parameters.Add("@ReviewerId", SqlDbType.UniqueIdentifier).Value = reviewerId;

        var items = new List<ReviewDashboardRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var findingsCount = reader.GetInt32(3);
            var closedFindingsCount = reader.GetInt32(4);
            items.Add(new ReviewDashboardRun(
                reader.GetGuid(0),
                reader.GetString(1),
                DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
                findingsCount,
                closedFindingsCount,
                reader.GetInt32(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<ReviewFeedbackEntry>> GetForRunAsync(
        Guid reviewerId,
        Guid reviewRunId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                feedback.ReviewerId,
                feedback.ReviewFindingId,
                CONVERT(varchar(64), finding.FindingFingerprint, 2),
                feedback.FeedbackStatus,
                feedback.ProjectName,
                feedback.RuleId,
                feedback.Severity,
                feedback.Message,
                feedback.Location,
                feedback.Rating,
                feedback.Comment,
                feedback.UpdatedUtc
            FROM dbo.BiAgentReviewFeedback AS feedback
            INNER JOIN dbo.BiAgentReviewFindings AS finding
                ON finding.ReviewFindingId = feedback.ReviewFindingId
            INNER JOIN dbo.BiAgentReviewRuns AS run
                ON run.ReviewRunId = finding.ReviewRunId
            WHERE feedback.ReviewerId = @ReviewerId
              AND run.ReviewRunId = @ReviewRunId
              AND run.ExpiresUtc > SYSUTCDATETIME()
            ORDER BY finding.FindingOrdinal;
            """;
        command.Parameters.Add("@ReviewerId", SqlDbType.UniqueIdentifier).Value = reviewerId;
        command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;

        var entries = new List<ReviewFeedbackEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new ReviewFeedbackEntry(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                DateTime.SpecifyKind(reader.GetDateTime(11), DateTimeKind.Utc)));
        }

        return entries;
    }

}
