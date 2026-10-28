IF OBJECT_ID(N'dbo.BiAgentReviewRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentReviewRuns
    (
        ReviewRunId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentReviewRuns PRIMARY KEY,
        ProjectName nvarchar(400) NOT NULL,
        TablesChecked int NOT NULL,
        MeasuresChecked int NOT NULL,
        RelationshipsChecked int NOT NULL,
        ChecksRun int NOT NULL,
        FindingsCount int NOT NULL,
        ReviewedUtc datetime2(3) NOT NULL,
        ExpiresUtc datetime2(3) NOT NULL,
        CONSTRAINT CK_BiAgentReviewRuns_Counts CHECK
            (TablesChecked >= 0 AND MeasuresChecked >= 0 AND RelationshipsChecked >= 0
             AND ChecksRun >= 0 AND FindingsCount >= 0),
        CONSTRAINT CK_BiAgentReviewRuns_Expiry CHECK (ExpiresUtc > ReviewedUtc)
    );
END;

IF OBJECT_ID(N'dbo.BiAgentReviewFindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentReviewFindings
    (
        ReviewRunId uniqueidentifier NOT NULL,
        FindingOrdinal int NOT NULL,
        RuleId nvarchar(100) NOT NULL,
        Severity nvarchar(20) NOT NULL,
        Message nvarchar(max) NOT NULL,
        Location nvarchar(1000) NOT NULL,
        Recommendation nvarchar(max) NOT NULL,
        CONSTRAINT PK_BiAgentReviewFindings PRIMARY KEY (ReviewRunId, FindingOrdinal),
        CONSTRAINT CK_BiAgentReviewFindings_Ordinal CHECK (FindingOrdinal >= 0),
        CONSTRAINT CK_BiAgentReviewFindings_Severity CHECK
            (Severity IN (N'Error', N'Warning', N'Information')),
        CONSTRAINT FK_BiAgentReviewFindings_ReviewRuns FOREIGN KEY (ReviewRunId)
            REFERENCES dbo.BiAgentReviewRuns (ReviewRunId)
            ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BiAgentReviewRuns', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewRuns')
         AND name = N'IX_BiAgentReviewRuns_ReviewedUtc')
BEGIN
    CREATE INDEX IX_BiAgentReviewRuns_ReviewedUtc
        ON dbo.BiAgentReviewRuns (ReviewedUtc DESC);
END;

IF OBJECT_ID(N'dbo.BiAgentReviewRuns', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM sys.indexes
       WHERE object_id = OBJECT_ID(N'dbo.BiAgentReviewRuns')
         AND name = N'IX_BiAgentReviewRuns_ExpiresUtc')
BEGIN
    CREATE INDEX IX_BiAgentReviewRuns_ExpiresUtc
        ON dbo.BiAgentReviewRuns (ExpiresUtc);
END;
