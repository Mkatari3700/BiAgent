SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunFindings', N'U') IS NULL
BEGIN
    THROW 51013, 'Apply Governance Audit migration 007 before migration 013.', 1;
END;

IF OBJECT_ID(N'dbo.CK_BiAgentGovernanceAuditRunFindings_Severity', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        DROP CONSTRAINT CK_BiAgentGovernanceAuditRunFindings_Severity;
END;

ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
    ADD CONSTRAINT CK_BiAgentGovernanceAuditRunFindings_Severity
        CHECK (Severity IN
            (N'Critical', N'High', N'Medium', N'Low', N'Error', N'Warning', N'Information'));

COMMIT TRANSACTION;
