SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRuns', N'U') IS NULL
   OR OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunFindings', N'U') IS NULL
BEGIN
    THROW 51015, 'Apply Governance Audit migrations 007 and 009 before migration 015.', 1;
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceExceptions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceExceptions
    (
        ExceptionId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        CheckId nvarchar(40) NOT NULL,
        ArtifactId uniqueidentifier NULL,
        ArtifactType nvarchar(40) NULL,
        ArtifactName nvarchar(256) NOT NULL,
        Owner nvarchar(256) NOT NULL,
        Reason nvarchar(2000) NOT NULL,
        ApprovedBy nvarchar(256) NOT NULL,
        ApprovedUtc datetimeoffset(7) NOT NULL,
        ExpiresUtc datetimeoffset(7) NOT NULL,
        Status nvarchar(20) NOT NULL,
        SourceRunId uniqueidentifier NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceExceptions PRIMARY KEY (ExceptionId),
        CONSTRAINT CK_BiAgentGovernanceExceptions_Status CHECK (Status IN (N'Approved', N'Revoked')),
        CONSTRAINT CK_BiAgentGovernanceExceptions_Expiry CHECK
            (ExpiresUtc > ApprovedUtc AND ExpiresUtc <= DATEADD(year, 1, ApprovedUtc)),
        CONSTRAINT CK_BiAgentGovernanceExceptions_RequiredFields CHECK
            (LEN(LTRIM(RTRIM(ArtifactName))) > 0 AND
             LEN(LTRIM(RTRIM(Owner))) > 0 AND
             LEN(LTRIM(RTRIM(Reason))) > 0)
    );

    CREATE INDEX IX_BiAgentGovernanceExceptions_Finding
        ON dbo.BiAgentGovernanceExceptions
            (WorkspaceId, CheckId, ArtifactId, ArtifactType, ArtifactName, Status, ExpiresUtc);
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceExceptionAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceExceptionAudit
    (
        AuditEventId bigint IDENTITY(1,1) NOT NULL,
        ExceptionId uniqueidentifier NOT NULL,
        EventType nvarchar(20) NOT NULL,
        EventUtc datetimeoffset(7) NOT NULL,
        Actor nvarchar(256) NOT NULL,
        Notes nvarchar(2000) NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceExceptionAudit PRIMARY KEY (AuditEventId),
        CONSTRAINT CK_BiAgentGovernanceExceptionAudit_EventType CHECK (EventType IN (N'Approved', N'Revoked')),
        CONSTRAINT FK_BiAgentGovernanceExceptionAudit_Exception
            FOREIGN KEY (ExceptionId)
            REFERENCES dbo.BiAgentGovernanceExceptions (ExceptionId)
    );

    CREATE INDEX IX_BiAgentGovernanceExceptionAudit_Exception
        ON dbo.BiAgentGovernanceExceptionAudit (ExceptionId, EventUtc, AuditEventId);
END;

COMMIT TRANSACTION;
