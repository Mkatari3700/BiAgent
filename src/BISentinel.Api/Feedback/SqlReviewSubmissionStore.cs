using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Feedback;

public sealed class SqlReviewSubmissionStore(string connectionString) : IReviewSubmissionStore
{
    public async Task<ReviewSubmission?> SubmitAsync(
        Guid reviewRunId,
        Guid submittedByUserId,
        IReadOnlyList<SubmitReviewFindingRequest> findings,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var expectedFindingIds = await ReadRunFindingIdsAsync(
            connection,
            transaction,
            reviewRunId,
            submittedByUserId,
            cancellationToken);
        if (expectedFindingIds is null || !expectedFindingIds.SetEquals(findings.Select(item => item.ReviewFindingId)))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var submissionId = Guid.NewGuid();
        await using (var command = new SqlCommand(
            """
            INSERT INTO dbo.BiAgentReviewSubmissions
                (SubmissionId, ReviewRunId, SubmittedByUserId)
            SELECT @SubmissionId, @ReviewRunId, @UserId
            WHERE NOT EXISTS
            (
                SELECT 1 FROM dbo.BiAgentReviewSubmissions WITH (UPDLOCK, HOLDLOCK)
                WHERE ReviewRunId = @ReviewRunId AND SubmittedByUserId = @UserId
            );
            SELECT @@ROWCOUNT;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
            command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
            command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = submittedByUserId;
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        foreach (var item in findings)
        {
            await using var command = new SqlCommand(
                """
                INSERT INTO dbo.BiAgentReviewSubmissionFindings
                    (SubmissionId, ReviewFindingId, SubmitterJustification)
                VALUES (@SubmissionId, @ReviewFindingId, @Justification);
                """,
                connection,
                transaction);
            command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
            command.Parameters.Add("@ReviewFindingId", SqlDbType.UniqueIdentifier).Value = item.ReviewFindingId;
            command.Parameters.Add("@Justification", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(item.Justification) ? DBNull.Value : item.Justification.Trim();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(submissionId, cancellationToken);
    }

    public async Task<ReviewSubmission?> GetMineAsync(
        Guid reviewRunId,
        Guid submittedByUserId,
        CancellationToken cancellationToken)
    {
        var submissionId = await GetSubmissionIdAsync(
            "WHERE submission.ReviewRunId = @Id AND submission.SubmittedByUserId = @UserId",
            reviewRunId,
            submittedByUserId,
            cancellationToken);
        return submissionId is null ? null : await GetAsync(submissionId.Value, cancellationToken);
    }

    public async Task<ReviewSubmission?> GetAsync(
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        ReviewSubmission? submission = null;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT submission.SubmissionId, submission.ReviewRunId, run.ProjectName,
                       submitter.DisplayName, submission.SubmittedUtc, submission.Status,
                       reviewer.DisplayName, submission.ReviewedUtc
                FROM dbo.BiAgentReviewSubmissions AS submission
                INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = submission.ReviewRunId
                INNER JOIN dbo.BiAgentApplicationUsers AS submitter ON submitter.UserId = submission.SubmittedByUserId
                LEFT JOIN dbo.BiAgentApplicationUsers AS reviewer ON reviewer.UserId = submission.ReviewedByUserId
                WHERE submission.SubmissionId = @SubmissionId AND run.ExpiresUtc > SYSUTCDATETIME();
                """;
            command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            submission = new ReviewSubmission(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTimeOffset(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7),
                []);
        }

        var submittedFindings = new List<ReviewSubmissionFinding>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT finding.ReviewFindingId, finding.RuleId, finding.Severity,
                       finding.Message, finding.Location, finding.Recommendation,
                       submissionFinding.SubmitterJustification,
                       submissionFinding.ReviewerDisposition, submissionFinding.ReviewerResponse
                FROM dbo.BiAgentReviewSubmissionFindings AS submissionFinding
                INNER JOIN dbo.BiAgentReviewFindings AS finding
                    ON finding.ReviewFindingId = submissionFinding.ReviewFindingId
                WHERE submissionFinding.SubmissionId = @SubmissionId
                ORDER BY finding.FindingOrdinal;
                """;
            command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                submittedFindings.Add(new ReviewSubmissionFinding(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        return submission with { Findings = submittedFindings };
    }

    public async Task<IReadOnlyList<ReviewSubmissionSummary>> GetQueueAsync(
        bool includeReviewed,
        CancellationToken cancellationToken)
    {
        var submissions = new List<ReviewSubmissionSummary>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT submission.SubmissionId, submission.ReviewRunId, run.ProjectName,
                   submitter.DisplayName, submission.SubmittedUtc, submission.Status,
                   COUNT(submissionFinding.ReviewFindingId)
            FROM dbo.BiAgentReviewSubmissions AS submission
            INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = submission.ReviewRunId
            INNER JOIN dbo.BiAgentApplicationUsers AS submitter ON submitter.UserId = submission.SubmittedByUserId
            LEFT JOIN dbo.BiAgentReviewSubmissionFindings AS submissionFinding
                ON submissionFinding.SubmissionId = submission.SubmissionId
            WHERE run.ExpiresUtc > SYSUTCDATETIME()
              AND (@IncludeReviewed = 1 OR submission.Status = N'Submitted')
            GROUP BY submission.SubmissionId, submission.ReviewRunId, run.ProjectName,
                     submitter.DisplayName, submission.SubmittedUtc, submission.Status
            ORDER BY CASE WHEN submission.Status = N'Submitted' THEN 0 ELSE 1 END,
                     submission.SubmittedUtc DESC;
            """;
        command.Parameters.Add("@IncludeReviewed", SqlDbType.Bit).Value = includeReviewed;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            submissions.Add(new ReviewSubmissionSummary(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTimeOffset(4),
                reader.GetString(5),
                reader.GetInt32(6)));
        }

        return submissions;
    }

    public async Task<IReadOnlyList<ReviewSubmissionSummary>> GetMyQueueAsync(
        Guid submittedByUserId,
        CancellationToken cancellationToken)
    {
        var submissions = new List<ReviewSubmissionSummary>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT submission.SubmissionId, submission.ReviewRunId, run.ProjectName,
                   submitter.DisplayName, submission.SubmittedUtc, submission.Status,
                   COUNT(submissionFinding.ReviewFindingId)
            FROM dbo.BiAgentReviewSubmissions AS submission
            INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = submission.ReviewRunId
            INNER JOIN dbo.BiAgentApplicationUsers AS submitter ON submitter.UserId = submission.SubmittedByUserId
            LEFT JOIN dbo.BiAgentReviewSubmissionFindings AS submissionFinding
                ON submissionFinding.SubmissionId = submission.SubmissionId
            WHERE submission.SubmittedByUserId = @UserId
              AND run.ExpiresUtc > SYSUTCDATETIME()
            GROUP BY submission.SubmissionId, submission.ReviewRunId, run.ProjectName,
                     submitter.DisplayName, submission.SubmittedUtc, submission.Status
            ORDER BY submission.SubmittedUtc DESC;
            """;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = submittedByUserId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            submissions.Add(new ReviewSubmissionSummary(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTimeOffset(4),
                reader.GetString(5),
                reader.GetInt32(6)));
        }

        return submissions;
    }

    public async Task<ReviewSubmission?> CompleteReviewAsync(
        Guid submissionId,
        Guid reviewerUserId,
        IReadOnlyList<ReviewSubmissionFindingRequest> findings,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var expectedFindingIds = await ReadSubmissionFindingIdsAsync(
            connection,
            transaction,
            submissionId,
            cancellationToken);
        if (expectedFindingIds is null || !expectedFindingIds.SetEquals(findings.Select(item => item.ReviewFindingId)))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using (var command = new SqlCommand(
            """
            UPDATE dbo.BiAgentReviewSubmissions
            SET Status = N'Reviewed', ReviewedByUserId = @ReviewerUserId,
                ReviewedUtc = SYSUTCDATETIME()
            WHERE SubmissionId = @SubmissionId AND Status = N'Submitted'
              AND EXISTS
              (
                  SELECT 1 FROM dbo.BiAgentReviewRuns AS run
                  WHERE run.ReviewRunId = BiAgentReviewSubmissions.ReviewRunId
                    AND run.ExpiresUtc > SYSUTCDATETIME()
              );
            SELECT @@ROWCOUNT;
            """,
            connection,
            transaction))
        {
            command.Parameters.Add("@ReviewerUserId", SqlDbType.UniqueIdentifier).Value = reviewerUserId;
            command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        foreach (var item in findings)
        {
            await using var command = new SqlCommand(
                """
                UPDATE dbo.BiAgentReviewSubmissionFindings
                SET ReviewerDisposition = @Disposition,
                    ReviewerResponse = @Response,
                    ReviewedUtc = SYSUTCDATETIME()
                WHERE SubmissionId = @SubmissionId AND ReviewFindingId = @ReviewFindingId;
                """,
                connection,
                transaction);
            command.Parameters.Add("@Disposition", SqlDbType.NVarChar, 30).Value = item.Disposition;
            command.Parameters.Add("@Response", SqlDbType.NVarChar, 1000).Value =
                string.IsNullOrWhiteSpace(item.Response) ? DBNull.Value : item.Response.Trim();
            command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
            command.Parameters.Add("@ReviewFindingId", SqlDbType.UniqueIdentifier).Value = item.ReviewFindingId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(submissionId, cancellationToken);
    }

    private async Task<Guid?> GetSubmissionIdAsync(
        string where,
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT submission.SubmissionId
            FROM dbo.BiAgentReviewSubmissions AS submission
            INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = submission.ReviewRunId
            {where} AND run.ExpiresUtc > SYSUTCDATETIME();
            """;
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@UserId", SqlDbType.UniqueIdentifier).Value = userId;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid submissionId ? submissionId : null;
    }

    private static async Task<HashSet<Guid>?> ReadRunFindingIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid reviewRunId,
        Guid uploadedByUserId,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        await using var command = new SqlCommand(
            """
            SELECT finding.ReviewFindingId
            FROM dbo.BiAgentReviewFindings AS finding
            INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = finding.ReviewRunId
            WHERE run.ReviewRunId = @ReviewRunId
              AND run.UploadedByUserId = @UploadedByUserId
              AND run.ExpiresUtc > SYSUTCDATETIME();
            """,
            connection,
            transaction);
        command.Parameters.Add("@ReviewRunId", SqlDbType.UniqueIdentifier).Value = reviewRunId;
        command.Parameters.Add("@UploadedByUserId", SqlDbType.UniqueIdentifier).Value = uploadedByUserId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids.Count > 0 ? ids : null;
    }

    private static async Task<HashSet<Guid>?> ReadSubmissionFindingIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        await using var command = new SqlCommand(
            """
            SELECT item.ReviewFindingId
            FROM dbo.BiAgentReviewSubmissionFindings AS item
            INNER JOIN dbo.BiAgentReviewSubmissions AS submission
                ON submission.SubmissionId = item.SubmissionId
            INNER JOIN dbo.BiAgentReviewRuns AS run ON run.ReviewRunId = submission.ReviewRunId
            WHERE item.SubmissionId = @SubmissionId AND submission.Status = N'Submitted'
              AND run.ExpiresUtc > SYSUTCDATETIME();
            """,
            connection,
            transaction);
        command.Parameters.Add("@SubmissionId", SqlDbType.UniqueIdentifier).Value = submissionId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids.Count > 0 ? ids : null;
    }
}
