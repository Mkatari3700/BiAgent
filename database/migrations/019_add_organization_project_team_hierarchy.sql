SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentOrganizations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentOrganizations
    (
        OrganizationId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentOrganizations PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_BiAgentOrganizations_IsEnabled DEFAULT (1),
        CreatedUtc datetimeoffset(3) NOT NULL CONSTRAINT DF_BiAgentOrganizations_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT UQ_BiAgentOrganizations_Name UNIQUE (Name),
        CONSTRAINT FK_BiAgentOrganizations_CreatedBy FOREIGN KEY (CreatedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );
END;

IF OBJECT_ID(N'dbo.BiAgentProjects', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentProjects
    (
        ProjectId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentProjects PRIMARY KEY,
        OrganizationId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_BiAgentProjects_IsEnabled DEFAULT (1),
        CreatedUtc datetimeoffset(3) NOT NULL CONSTRAINT DF_BiAgentProjects_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT UQ_BiAgentProjects_Organization_Name UNIQUE (OrganizationId, Name),
        CONSTRAINT FK_BiAgentProjects_Organization FOREIGN KEY (OrganizationId)
            REFERENCES dbo.BiAgentOrganizations (OrganizationId),
        CONSTRAINT FK_BiAgentProjects_CreatedBy FOREIGN KEY (CreatedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );
END;

IF OBJECT_ID(N'dbo.BiAgentTeams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentTeams
    (
        TeamId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentTeams PRIMARY KEY,
        ProjectId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        IsEnabled bit NOT NULL CONSTRAINT DF_BiAgentTeams_IsEnabled DEFAULT (1),
        CreatedUtc datetimeoffset(3) NOT NULL CONSTRAINT DF_BiAgentTeams_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT UQ_BiAgentTeams_Project_Name UNIQUE (ProjectId, Name),
        CONSTRAINT FK_BiAgentTeams_Project FOREIGN KEY (ProjectId)
            REFERENCES dbo.BiAgentProjects (ProjectId),
        CONSTRAINT FK_BiAgentTeams_CreatedBy FOREIGN KEY (CreatedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );
END;

IF OBJECT_ID(N'dbo.BiAgentUserScopeAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentUserScopeAssignments
    (
        AssignmentId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentUserScopeAssignments PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        ScopeType nvarchar(20) NOT NULL,
        OrganizationId uniqueidentifier NULL,
        ProjectId uniqueidentifier NULL,
        TeamId uniqueidentifier NULL,
        RoleName nvarchar(20) NOT NULL,
        AssignedUtc datetimeoffset(3) NOT NULL CONSTRAINT DF_BiAgentUserScopeAssignments_AssignedUtc DEFAULT SYSUTCDATETIME(),
        AssignedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT CK_BiAgentUserScopeAssignments_ScopeRole CHECK
        (
            (ScopeType = N'Organization' AND OrganizationId IS NOT NULL AND ProjectId IS NULL AND TeamId IS NULL
                AND RoleName IN (N'OrgLead', N'ReadOnly'))
            OR (ScopeType = N'Project' AND OrganizationId IS NULL AND ProjectId IS NOT NULL AND TeamId IS NULL
                AND RoleName IN (N'ProjectLead', N'ReadOnly'))
            OR (ScopeType = N'Team' AND OrganizationId IS NULL AND ProjectId IS NULL AND TeamId IS NOT NULL
                AND RoleName IN (N'TeamLead', N'ReadOnly'))
        ),
        CONSTRAINT FK_BiAgentUserScopeAssignments_User FOREIGN KEY (UserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentUserScopeAssignments_AssignedBy FOREIGN KEY (AssignedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentUserScopeAssignments_Organization FOREIGN KEY (OrganizationId)
            REFERENCES dbo.BiAgentOrganizations (OrganizationId),
        CONSTRAINT FK_BiAgentUserScopeAssignments_Project FOREIGN KEY (ProjectId)
            REFERENCES dbo.BiAgentProjects (ProjectId),
        CONSTRAINT FK_BiAgentUserScopeAssignments_Team FOREIGN KEY (TeamId)
            REFERENCES dbo.BiAgentTeams (TeamId)
    );

    CREATE UNIQUE INDEX UX_BiAgentUserScopeAssignments_Organization
        ON dbo.BiAgentUserScopeAssignments (UserId, OrganizationId, RoleName)
        WHERE OrganizationId IS NOT NULL;
    CREATE UNIQUE INDEX UX_BiAgentUserScopeAssignments_Project
        ON dbo.BiAgentUserScopeAssignments (UserId, ProjectId, RoleName)
        WHERE ProjectId IS NOT NULL;
    CREATE UNIQUE INDEX UX_BiAgentUserScopeAssignments_Team
        ON dbo.BiAgentUserScopeAssignments (UserId, TeamId, RoleName)
        WHERE TeamId IS NOT NULL;
    CREATE INDEX IX_BiAgentUserScopeAssignments_User
        ON dbo.BiAgentUserScopeAssignments (UserId, ScopeType);
END;

IF COL_LENGTH(N'dbo.BiAgentReviewRuns', N'OrganizationId') IS NULL
    ALTER TABLE dbo.BiAgentReviewRuns ADD OrganizationId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BiAgentReviewRuns', N'ProjectScopeId') IS NULL
    ALTER TABLE dbo.BiAgentReviewRuns ADD ProjectScopeId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BiAgentReviewRuns', N'TeamScopeId') IS NULL
    ALTER TABLE dbo.BiAgentReviewRuns ADD TeamScopeId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRuns', N'OrganizationId') IS NULL
    ALTER TABLE dbo.BiAgentGovernanceAuditRuns ADD OrganizationId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRuns', N'ProjectScopeId') IS NULL
    ALTER TABLE dbo.BiAgentGovernanceAuditRuns ADD ProjectScopeId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BiAgentGovernanceAuditRuns', N'TeamScopeId') IS NULL
    ALTER TABLE dbo.BiAgentGovernanceAuditRuns ADD TeamScopeId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.BiAgentSupportRequests', N'OrganizationId') IS NULL
    ALTER TABLE dbo.BiAgentSupportRequests ADD OrganizationId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BiAgentSupportRequests', N'ProjectScopeId') IS NULL
    ALTER TABLE dbo.BiAgentSupportRequests ADD ProjectScopeId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.BiAgentSupportRequests', N'TeamScopeId') IS NULL
    ALTER TABLE dbo.BiAgentSupportRequests ADD TeamScopeId uniqueidentifier NULL;

COMMIT TRANSACTION;
