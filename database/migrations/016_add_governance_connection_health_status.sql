SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunReportConnections', N'U') IS NULL
BEGIN
    THROW 51016, 'Apply Governance Audit migration 014 before migration 016.', 1;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunReportConnections', N'GatewayStatus') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunReportConnections
        ADD GatewayStatus nvarchar(100) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunReportConnections_GatewayStatus DEFAULT N'Unknown' WITH VALUES;
END;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRunReportConnections', N'GatewayStatusDetail') IS NULL
BEGIN
    ALTER TABLE dbo.BiAgentGovernanceAuditRunReportConnections
        ADD GatewayStatusDetail nvarchar(2000) NOT NULL
            CONSTRAINT DF_BiAgentGovernanceAuditRunReportConnections_GatewayStatusDetail
            DEFAULT N'Gateway status was not returned.' WITH VALUES;
END;

COMMIT TRANSACTION;
