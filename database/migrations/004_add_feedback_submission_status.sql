SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.BiAgentReviewFeedback', N'FeedbackStatus') IS NULL
    BEGIN
        ALTER TABLE dbo.BiAgentReviewFeedback
            ADD FeedbackStatus nvarchar(10) NOT NULL
                CONSTRAINT DF_BiAgentReviewFeedback_FeedbackStatus DEFAULT N'Closed';
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.BiAgentReviewFeedback')
          AND name = N'CK_BiAgentReviewFeedback_FeedbackStatus')
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.BiAgentReviewFeedback
                ADD CONSTRAINT CK_BiAgentReviewFeedback_FeedbackStatus
                    CHECK (FeedbackStatus IN (N''Open'', N''Closed''));';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
