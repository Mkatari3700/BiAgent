IF OBJECT_ID(N'dbo.BiAgentReviewFeedback', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentReviewFeedback
    (
        FeedbackId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_BiAgentReviewFeedback PRIMARY KEY,
        ReviewerId uniqueidentifier NOT NULL,
        ProjectName nvarchar(200) NOT NULL,
        FindingKey binary(32) NOT NULL,
        RuleId nvarchar(50) NOT NULL,
        Severity nvarchar(20) NOT NULL,
        Message nvarchar(1000) NOT NULL,
        Location nvarchar(300) NOT NULL,
        Rating nvarchar(20) NULL,
        Comment nvarchar(500) NULL,
        UpdatedUtc datetime2(3) NOT NULL
            CONSTRAINT DF_BiAgentReviewFeedback_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_BiAgentReviewFeedback_Rating
            CHECK (Rating IS NULL OR Rating IN (N'helpful', N'incorrect', N'not-applicable')),
        CONSTRAINT CK_BiAgentReviewFeedback_HasFeedback
            CHECK (Rating IS NOT NULL OR Comment IS NOT NULL)
    );

    CREATE UNIQUE INDEX UX_BiAgentReviewFeedback_Reviewer_Project_Finding
        ON dbo.BiAgentReviewFeedback (ReviewerId, ProjectName, FindingKey);

    CREATE INDEX IX_BiAgentReviewFeedback_UpdatedUtc
        ON dbo.BiAgentReviewFeedback (UpdatedUtc DESC);
END;
