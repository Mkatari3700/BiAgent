SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunArtifacts', N'U') IS NULL
BEGIN
    THROW 51010, 'Apply Governance Audit migration 007 before migration 010.', 1;
END;

IF OBJECT_ID(N'dbo.CK_BiAgentGovernanceAuditRunArtifacts_Type', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunArtifacts
        DROP CONSTRAINT CK_BiAgentGovernanceAuditRunArtifacts_Type;
END;

ALTER TABLE dbo.BiAgentGovernanceAuditRunArtifacts
    ADD CONSTRAINT CK_BiAgentGovernanceAuditRunArtifacts_Type
        CHECK (ArtifactType IN (N'Report', N'Semantic model', N'Dashboard'));

COMMIT TRANSACTION;
