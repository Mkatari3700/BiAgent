SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRuns', N'U') IS NULL
BEGIN
    THROW 51014, 'Apply Governance Audit migration 007 before migration 014.', 1;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunGlobalArtifacts', N'Status') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunGlobalArtifacts
        ADD Status nvarchar(100) NULL;
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunReportConnections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRunReportConnections
    (
        ConnectionId bigint IDENTITY(1,1) NOT NULL,
        RunId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        ReportId uniqueidentifier NOT NULL,
        ReportName nvarchar(256) NOT NULL,
        SemanticModelName nvarchar(256) NULL,
        SourceType nvarchar(100) NULL,
        ConnectionDetails nvarchar(2000) NULL,
        GatewayId uniqueidentifier NULL,
        Status nvarchar(100) NOT NULL,
        StatusDetail nvarchar(2000) NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditRunReportConnections PRIMARY KEY (ConnectionId),
        CONSTRAINT FK_BiAgentGovernanceAuditRunReportConnections_Run
            FOREIGN KEY (RunId)
            REFERENCES dbo.BiAgentGovernanceAuditRuns (RunId)
            ON DELETE CASCADE
    );

    CREATE INDEX IX_BiAgentGovernanceAuditRunReportConnections_Run
        ON dbo.BiAgentGovernanceAuditRunReportConnections (RunId, WorkspaceId);
END;

COMMIT TRANSACTION;
