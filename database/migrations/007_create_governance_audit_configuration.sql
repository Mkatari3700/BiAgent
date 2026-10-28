SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditConfiguration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditConfiguration
    (
        ConfigurationId int NOT NULL
            CONSTRAINT PK_BiAgentGovernanceAuditConfiguration PRIMARY KEY
            CONSTRAINT CK_BiAgentGovernanceAuditConfiguration_Singleton CHECK (ConfigurationId = 1),
        Version int NOT NULL,
        UpdatedUtc datetime2(3) NOT NULL,
        UpdatedBy nvarchar(256) NOT NULL
    );

    INSERT INTO dbo.BiAgentGovernanceAuditConfiguration
        (ConfigurationId, Version, UpdatedUtc, UpdatedBy)
    VALUES (1, 1, SYSUTCDATETIME(), N'System');
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditWorkspaces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditWorkspaces
    (
        ConfigurationId int NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditWorkspaces
            PRIMARY KEY (ConfigurationId, WorkspaceId),
        CONSTRAINT FK_BiAgentGovernanceAuditWorkspaces_Configuration
            FOREIGN KEY (ConfigurationId)
            REFERENCES dbo.BiAgentGovernanceAuditConfiguration (ConfigurationId)
            ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditConfigurationAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditConfigurationAudit
    (
        AuditId bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_BiAgentGovernanceAuditConfigurationAudit PRIMARY KEY,
        Version int NOT NULL,
        ChangedUtc datetime2(3) NOT NULL,
        ChangedBy nvarchar(256) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditConfigurationAuditWorkspaces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditConfigurationAuditWorkspaces
    (
        AuditId bigint NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditConfigurationAuditWorkspaces
            PRIMARY KEY (AuditId, WorkspaceId),
        CONSTRAINT FK_BiAgentGovernanceAuditConfigurationAuditWorkspaces_Audit
            FOREIGN KEY (AuditId)
            REFERENCES dbo.BiAgentGovernanceAuditConfigurationAudit (AuditId)
            ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRuns
    (
        RunId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentGovernanceAuditRuns PRIMARY KEY,
        ScannedUtc datetime2(3) NOT NULL,
        ScannedBy nvarchar(256) NOT NULL
    );

    CREATE INDEX IX_BiAgentGovernanceAuditRuns_ScannedUtc
        ON dbo.BiAgentGovernanceAuditRuns (ScannedUtc DESC);
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunWorkspaces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRunWorkspaces
    (
        RunId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        WorkspaceName nvarchar(256) NOT NULL,
        IsReadOnly bit NOT NULL,
        IsOnDedicatedCapacity bit NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditRunWorkspaces
            PRIMARY KEY (RunId, WorkspaceId),
        CONSTRAINT FK_BiAgentGovernanceAuditRunWorkspaces_Run
            FOREIGN KEY (RunId) REFERENCES dbo.BiAgentGovernanceAuditRuns (RunId)
            ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRunUsers
    (
        RunId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        Identifier nvarchar(256) NOT NULL,
        DisplayName nvarchar(256) NULL,
        EmailAddress nvarchar(256) NULL,
        PrincipalType nvarchar(40) NOT NULL,
        AccessRight nvarchar(40) NOT NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditRunUsers
            PRIMARY KEY (RunId, WorkspaceId, Identifier),
        CONSTRAINT FK_BiAgentGovernanceAuditRunUsers_Workspace
            FOREIGN KEY (RunId, WorkspaceId)
            REFERENCES dbo.BiAgentGovernanceAuditRunWorkspaces (RunId, WorkspaceId)
            ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunArtifacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRunArtifacts
    (
        RunId uniqueidentifier NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        ArtifactId uniqueidentifier NOT NULL,
        ArtifactName nvarchar(256) NOT NULL,
        ArtifactType nvarchar(40) NOT NULL,
        DatasetId uniqueidentifier NULL,
        ConfiguredBy nvarchar(256) NULL,
        WebUrl nvarchar(2048) NULL,
        CONSTRAINT PK_BiAgentGovernanceAuditRunArtifacts
            PRIMARY KEY (RunId, WorkspaceId, ArtifactId),
        CONSTRAINT CK_BiAgentGovernanceAuditRunArtifacts_Type
            CHECK (ArtifactType IN (N'Report', N'Semantic model')),
        CONSTRAINT FK_BiAgentGovernanceAuditRunArtifacts_Workspace
            FOREIGN KEY (RunId, WorkspaceId)
            REFERENCES dbo.BiAgentGovernanceAuditRunWorkspaces (RunId, WorkspaceId)
            ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.BiAgentGovernanceAuditRunFindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentGovernanceAuditRunFindings
    (
        FindingId bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_BiAgentGovernanceAuditRunFindings PRIMARY KEY,
        RunId uniqueidentifier NOT NULL,
        Code nvarchar(40) NOT NULL,
        Severity nvarchar(20) NOT NULL,
        WorkspaceId uniqueidentifier NOT NULL,
        WorkspaceName nvarchar(256) NOT NULL,
        ArtifactId uniqueidentifier NULL,
        Title nvarchar(256) NOT NULL,
        Detail nvarchar(2000) NOT NULL,
        CONSTRAINT CK_BiAgentGovernanceAuditRunFindings_Severity
            CHECK (Severity IN (N'Error', N'Warning', N'Information')),
        CONSTRAINT FK_BiAgentGovernanceAuditRunFindings_Run
            FOREIGN KEY (RunId) REFERENCES dbo.BiAgentGovernanceAuditRuns (RunId)
            ON DELETE CASCADE
    );
END;

COMMIT TRANSACTION;
