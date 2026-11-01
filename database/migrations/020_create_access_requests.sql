SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentAccessRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentAccessRequests
    (
        AccessRequestId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentAccessRequests PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        ScopeType nvarchar(20) NOT NULL,
        OrganizationId uniqueidentifier NULL,
        ProjectId uniqueidentifier NULL,
        TeamId uniqueidentifier NULL,
        RequestedRole nvarchar(20) NOT NULL,
        Message nvarchar(2000) NULL,
        Status nvarchar(20) NOT NULL
            CONSTRAINT DF_BiAgentAccessRequests_Status DEFAULT (N'Pending'),
        ResolutionMessage nvarchar(2000) NULL,
        RespondedByUserId uniqueidentifier NULL,
        SubmittedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentAccessRequests_SubmittedUtc DEFAULT SYSUTCDATETIME(),
        ResolvedUtc datetimeoffset(3) NULL,
        CONSTRAINT CK_BiAgentAccessRequests_ScopeRole CHECK
        (
            (ScopeType = N'Organization' AND OrganizationId IS NOT NULL AND ProjectId IS NULL AND TeamId IS NULL
                AND RequestedRole IN (N'OrgLead', N'ReadOnly'))
            OR (ScopeType = N'Project' AND OrganizationId IS NULL AND ProjectId IS NOT NULL AND TeamId IS NULL
                AND RequestedRole IN (N'ProjectLead', N'ReadOnly'))
            OR (ScopeType = N'Team' AND OrganizationId IS NULL AND ProjectId IS NULL AND TeamId IS NOT NULL
                AND RequestedRole IN (N'TeamLead', N'ReadOnly'))
        ),
        CONSTRAINT CK_BiAgentAccessRequests_Status CHECK (Status IN (N'Pending', N'Approved', N'Declined')),
        CONSTRAINT FK_BiAgentAccessRequests_User FOREIGN KEY (UserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentAccessRequests_Organization FOREIGN KEY (OrganizationId)
            REFERENCES dbo.BiAgentOrganizations (OrganizationId),
        CONSTRAINT FK_BiAgentAccessRequests_Project FOREIGN KEY (ProjectId)
            REFERENCES dbo.BiAgentProjects (ProjectId),
        CONSTRAINT FK_BiAgentAccessRequests_Team FOREIGN KEY (TeamId)
            REFERENCES dbo.BiAgentTeams (TeamId),
        CONSTRAINT FK_BiAgentAccessRequests_RespondedBy FOREIGN KEY (RespondedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );

    CREATE INDEX IX_BiAgentAccessRequests_User
        ON dbo.BiAgentAccessRequests (UserId, SubmittedUtc DESC);
    CREATE INDEX IX_BiAgentAccessRequests_Pending
        ON dbo.BiAgentAccessRequests (Status, SubmittedUtc);
    CREATE UNIQUE INDEX UX_BiAgentAccessRequests_PendingOrganization
        ON dbo.BiAgentAccessRequests (UserId, OrganizationId, RequestedRole)
        WHERE Status = N'Pending' AND OrganizationId IS NOT NULL;
    CREATE UNIQUE INDEX UX_BiAgentAccessRequests_PendingProject
        ON dbo.BiAgentAccessRequests (UserId, ProjectId, RequestedRole)
        WHERE Status = N'Pending' AND ProjectId IS NOT NULL;
    CREATE UNIQUE INDEX UX_BiAgentAccessRequests_PendingTeam
        ON dbo.BiAgentAccessRequests (UserId, TeamId, RequestedRole)
        WHERE Status = N'Pending' AND TeamId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.BiAgentAccessRequestSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentAccessRequestSettings
    (
        SettingsId tinyint NOT NULL
            CONSTRAINT PK_BiAgentAccessRequestSettings PRIMARY KEY
            CONSTRAINT CK_BiAgentAccessRequestSettings_Singleton CHECK (SettingsId = 1),
        EscalationDays int NOT NULL
            CONSTRAINT DF_BiAgentAccessRequestSettings_EscalationDays DEFAULT (1)
            CONSTRAINT CK_BiAgentAccessRequestSettings_EscalationDays CHECK (EscalationDays BETWEEN 1 AND 30),
        UpdatedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentAccessRequestSettings_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedByUserId uniqueidentifier NULL,
        CONSTRAINT FK_BiAgentAccessRequestSettings_UpdatedBy FOREIGN KEY (UpdatedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );

    INSERT INTO dbo.BiAgentAccessRequestSettings (SettingsId, EscalationDays)
    VALUES (1, 1);
END;

IF OBJECT_ID(N'dbo.BiAgentAccessRequestNotifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentAccessRequestNotifications
    (
        NotificationId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentAccessRequestNotifications PRIMARY KEY,
        AccessRequestId uniqueidentifier NOT NULL,
        RecipientUserId uniqueidentifier NOT NULL,
        TemplateKey nvarchar(40) NOT NULL,
        NotificationDate date NOT NULL,
        CreatedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentAccessRequestNotifications_CreatedUtc DEFAULT SYSUTCDATETIME(),
        AttemptCount int NOT NULL
            CONSTRAINT DF_BiAgentAccessRequestNotifications_AttemptCount DEFAULT (0),
        LastAttemptUtc datetimeoffset(3) NULL,
        SentUtc datetimeoffset(3) NULL,
        LastError nvarchar(2000) NULL,
        CONSTRAINT UQ_BiAgentAccessRequestNotifications_Daily
            UNIQUE (AccessRequestId, RecipientUserId, TemplateKey, NotificationDate),
        CONSTRAINT FK_BiAgentAccessRequestNotifications_Request FOREIGN KEY (AccessRequestId)
            REFERENCES dbo.BiAgentAccessRequests (AccessRequestId),
        CONSTRAINT FK_BiAgentAccessRequestNotifications_Recipient FOREIGN KEY (RecipientUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );

    CREATE INDEX IX_BiAgentAccessRequestNotifications_Pending
        ON dbo.BiAgentAccessRequestNotifications (SentUtc, LastAttemptUtc, CreatedUtc);
END;

COMMIT TRANSACTION;
