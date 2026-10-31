SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRuns', N'U') IS NULL
BEGIN
    THROW 51011, 'Apply Governance Audit migration 007 before migration 011.', 1;
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunGlobalArtifacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRunGlobalArtifacts
    (
        RunId uniqueidentifier NOT NULL,
        ArtifactId uniqueidentifier NOT NULL,
        ArtifactName nvarchar(256) NOT NULL,
        ArtifactType nvarchar(40) NOT NULL,
        ConfiguredBy nvarchar(256) NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditRunGlobalArtifacts
            PRIMARY KEY (RunId, ArtifactType, ArtifactId),
        CONSTRAINT CK_BiAgentGovernanceAuditRunGlobalArtifacts_Type
            CHECK (ArtifactType IN (N'App', N'Gateway')),
        CONSTRAINT FK_BiAgentGovernanceAuditRunGlobalArtifacts_Run
            FOREIGN KEY (RunId)
            REFERENCES dbo.BiAgentGovernanceAuditRuns (RunId)
            ON DELETE CASCADE
    );
END;

COMMIT TRANSACTION;
