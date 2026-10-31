SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'ArtifactType') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD ArtifactType nvarchar(40) NULL;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'ArtifactName') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD ArtifactName nvarchar(256) NULL;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'CheckVersion') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD CheckVersion nvarchar(20) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_CheckVersion DEFAULT (N'1.0') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'CheckName') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD CheckName nvarchar(256) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_CheckName DEFAULT (N'Legacy governance finding') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'RuleOwner') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD RuleOwner nvarchar(256) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_RuleOwner DEFAULT (N'BI Governance Team') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'EvidenceSource') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD EvidenceSource nvarchar(512) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_EvidenceSource DEFAULT (N'Power BI REST API') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'EvidenceTimestamp') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD EvidenceTimestamp datetime2(3) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_EvidenceTimestamp DEFAULT (SYSUTCDATETIME()) WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'Status') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD Status nvarchar(40) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_Status DEFAULT (N'FAIL') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'EvaluationLogic') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD EvaluationLogic nvarchar(1000) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_EvaluationLogic DEFAULT (N'Legacy finding; evaluation logic was not recorded.') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'ExceptionRules') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD ExceptionRules nvarchar(1000) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_ExceptionRules DEFAULT (N'No exception was recorded for this finding.') WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'ExpectedValue') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD ExpectedValue nvarchar(2000) NULL;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'ActualValue') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD ActualValue nvarchar(2000) NULL;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunFindings', N'EvidenceJson') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunFindings
        ADD EvidenceJson nvarchar(max) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunFindings_EvidenceJson DEFAULT (N'{}') WITH VALUES;
END;

COMMIT TRANSACTION;
