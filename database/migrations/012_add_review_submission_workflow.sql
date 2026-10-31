SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.BiAgentReviewRuns', N'UploadedByUserId') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentReviewRuns
        ADD UploadedByUserId uniqueidentifier NULL;

    ALTER TABLE dbo.BiAgentReviewRuns
        ADD CONSTRAINT FK_BiAgentReviewRuns_Uploader
            FOREIGN KEY (UploadedByUserId) REFERENCES dbo.BiAgentApplicationUsers (UserId);

    CREATE INDEX IX_BiAgentReviewRuns_Uploader_ReviewedUtc
        ON dbo.BiAgentReviewRuns (UploadedByUserId, ReviewedUtc DESC);
END;

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.BiAgentApplicationUserRoles')
      AND name = N'CK_BiAgentApplicationUserRoles_RoleName'
)
BEGIN
    ALTER TABLE dbo.BiAgentApplicationUserRoles
        DROP CONSTRAINT CK_BiAgentApplicationUserRoles_RoleName;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.BiAgentApplicationUserRoles')
      AND name = N'CK_BiAgentApplicationUserRoles_RoleName'
)
BEGIN
    ALTER TABLE dbo.BiAgentApplicationUserRoles
        ADD CONSTRAINT CK_BiAgentApplicationUserRoles_RoleName
        CHECK (RoleName IN (N'ApplicationAdmin', N'ReviewManager', N'ReviewLead'));
END;

IF OBJECT_ID(N'dbo.BiAgentReviewSubmissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentReviewSubmissions
    (
        SubmissionId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentReviewSubmissions PRIMARY KEY,
        ReviewRunId uniqueidentifier NOT NULL,
        SubmittedByUserId uniqueidentifier NOT NULL,
        Status nvarchar(20) NOT NULL
            CONSTRAINT DF_BiAgentReviewSubmissions_Status DEFAULT N'Submitted',
        SubmittedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentReviewSubmissions_SubmittedUtc DEFAULT SYSUTCDATETIME(),
        ReviewedByUserId uniqueidentifier NULL,
        ReviewedUtc datetimeoffset(3) NULL,
        CONSTRAINT CK_BiAgentReviewSubmissions_Status
            CHECK (Status IN (N'Submitted', N'Reviewed')),
        CONSTRAINT FK_BiAgentReviewSubmissions_Run
            FOREIGN KEY (ReviewRunId) REFERENCES dbo.BiAgentReviewRuns (ReviewRunId)
            ON DELETE CASCADE,
        CONSTRAINT FK_BiAgentReviewSubmissions_Submitter
            FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentReviewSubmissions_Reviewer
            FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );

    CREATE UNIQUE INDEX UX_BiAgentReviewSubmissions_Run_Submitter
        ON dbo.BiAgentReviewSubmissions (ReviewRunId, SubmittedByUserId);
    CREATE INDEX IX_BiAgentReviewSubmissions_Status_SubmittedUtc
        ON dbo.BiAgentReviewSubmissions (Status, SubmittedUtc DESC);
END;

IF OBJECT_ID(N'dbo.BiAgentReviewSubmissionFindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentReviewSubmissionFindings
    (
        SubmissionId uniqueidentifier NOT NULL,
        ReviewFindingId uniqueidentifier NOT NULL,
        SubmitterJustification nvarchar(1000) NULL,
        ReviewerDisposition nvarchar(30) NULL,
        ReviewerResponse nvarchar(1000) NULL,
        ReviewedUtc datetimeoffset(3) NULL,
        CONSTRAINT PK_BiAgentReviewSubmissionFindings
            PRIMARY KEY (SubmissionId, ReviewFindingId),
        CONSTRAINT CK_BiAgentReviewSubmissionFindings_Disposition
            CHECK (ReviewerDisposition IS NULL OR ReviewerDisposition IN
                (N'Accepted', N'NeedsChanges', N'Noted', N'IncorrectFinding')),
        CONSTRAINT FK_BiAgentReviewSubmissionFindings_Submission
            FOREIGN KEY (SubmissionId) REFERENCES dbo.BiAgentReviewSubmissions (SubmissionId)
            ON DELETE CASCADE,
        CONSTRAINT FK_BiAgentReviewSubmissionFindings_Finding
            FOREIGN KEY (ReviewFindingId) REFERENCES dbo.BiAgentReviewFindings (ReviewFindingId)
    );
END;

COMMIT TRANSACTION;
