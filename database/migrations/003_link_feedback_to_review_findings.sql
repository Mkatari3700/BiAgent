SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.BiAgentReviewRuns', N'ProjectId') IS NULL
    BEGIN
        ALTER TABLE dbo.BiAgentReviewRuns
            ADD ProjectId nvarchar(100) NOT NULL
                CONSTRAINT DF_BiAgentReviewRuns_ProjectId DEFAULT N'legacy';
    END;

    IF COL_LENGTH(N'dbo.BiAgentReviewFindings', N'ReviewFindingId') IS NULL
    BEGIN
        ALTER TABLE dbo.BiAgentReviewFindings
            ADD ReviewFindingId uniqueidentifier NOT NULL
                CONSTRAINT DF_BiAgentReviewFindings_ReviewFindingId DEFAULT NEWID();
    END;

    IF COL_LENGTH(N'dbo.BiAgentReviewFindings', N'FindingFingerprint') IS NULL
    BEGIN
        ALTER TABLE dbo.BiAgentReviewFindings
            ADD FindingFingerprint binary(32) NULL;
    END;

    EXEC sys.sp_executesql N'
        UPDATE finding
        SET FindingFingerprint = HASHBYTES(
            ''SHA2_256'',
            CONVERT(varbinary(max), CONCAT(run.ProjectId, NCHAR(10), finding.RuleId, NCHAR(10), finding.Location)))
        FROM dbo.BiAgentReviewFindings AS finding
        INNER JOIN dbo.BiAgentReviewRuns AS run
            ON run.ReviewRunId = finding.ReviewRunId
        WHERE finding.FindingFingerprint IS NULL;';

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewFindings')
          AND name = N'FindingFingerprint'
          AND is_nullable = 1)
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.BiAgentReviewFindings
                ALTER COLUMN FindingFingerprint binary(32) NOT NULL;';
    END;

    IF COL_LENGTH(N'dbo.BiAgentReviewFeedback', N'ReviewFindingId') IS NULL
    BEGIN
        ALTER TABLE dbo.BiAgentReviewFeedback
            ADD ReviewFindingId uniqueidentifier NULL;
    END;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewFeedback')
          AND name = N'UX_BiAgentReviewFeedback_Reviewer_Project_Finding')
    BEGIN
        DROP INDEX UX_BiAgentReviewFeedback_Reviewer_Project_Finding
            ON dbo.BiAgentReviewFeedback;
    END;

    ALTER TABLE dbo.BiAgentReviewFeedback
        ALTER COLUMN ProjectName nvarchar(400) NOT NULL;
    ALTER TABLE dbo.BiAgentReviewFeedback
        ALTER COLUMN RuleId nvarchar(100) NOT NULL;
    ALTER TABLE dbo.BiAgentReviewFeedback
        ALTER COLUMN Message nvarchar(max) NOT NULL;
    ALTER TABLE dbo.BiAgentReviewFeedback
        ALTER COLUMN Location nvarchar(1000) NOT NULL;

    EXEC sys.sp_executesql N'
        UPDATE feedback
        SET ReviewFindingId = match.ReviewFindingId
        FROM dbo.BiAgentReviewFeedback AS feedback
        CROSS APPLY
        (
            SELECT TOP (1) finding.ReviewFindingId
            FROM dbo.BiAgentReviewRuns AS run
            INNER JOIN dbo.BiAgentReviewFindings AS finding
                ON finding.ReviewRunId = run.ReviewRunId
            WHERE run.ProjectName = feedback.ProjectName
              AND finding.RuleId = feedback.RuleId
              AND finding.Message = feedback.Message
              AND finding.Location = feedback.Location
            ORDER BY run.ReviewedUtc DESC, finding.FindingOrdinal
        ) AS match
        WHERE feedback.ReviewFindingId IS NULL;';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewFindings')
          AND name = N'UQ_BiAgentReviewFindings_ReviewFindingId')
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UQ_BiAgentReviewFindings_ReviewFindingId
                ON dbo.BiAgentReviewFindings (ReviewFindingId);';
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_BiAgentReviewFeedback_ReviewFindings')
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.BiAgentReviewFeedback
                ADD CONSTRAINT FK_BiAgentReviewFeedback_ReviewFindings
                    FOREIGN KEY (ReviewFindingId)
                    REFERENCES dbo.BiAgentReviewFindings (ReviewFindingId)
                    ON DELETE CASCADE;';
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewFindings')
          AND name = N'IX_BiAgentReviewFindings_Fingerprint')
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE INDEX IX_BiAgentReviewFindings_Fingerprint
                ON dbo.BiAgentReviewFindings (FindingFingerprint, ReviewRunId);';
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewFeedback')
          AND name = N'UX_BiAgentReviewFeedback_Reviewer_ReviewFinding')
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UX_BiAgentReviewFeedback_Reviewer_ReviewFinding
                ON dbo.BiAgentReviewFeedback (ReviewerId, ReviewFindingId)
                WHERE ReviewFindingId IS NOT NULL;';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
