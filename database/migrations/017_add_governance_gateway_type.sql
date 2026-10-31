SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunGlobalArtifacts', N'U') IS NULL
BEGIN
    THROW 51017, 'Apply Governance Audit migration 011 before migration 017.', 1;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunGlobalArtifacts', N'GatewayType') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunGlobalArtifacts
        ADD GatewayType nvarchar(100) NULL;
END;

COMMIT TRANSACTION;
