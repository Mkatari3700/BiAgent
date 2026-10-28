SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentApplicationUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentApplicationUsers
    (
        UserId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentApplicationUsers PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        EntraObjectId uniqueidentifier NOT NULL,
        DisplayName nvarchar(256) NOT NULL,
        EmailAddress nvarchar(256) NULL,
        IsEnabled bit NOT NULL
            CONSTRAINT DF_BiAgentApplicationUsers_IsEnabled DEFAULT (1),
        FirstSeenUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentApplicationUsers_FirstSeenUtc DEFAULT (SYSUTCDATETIME()),
        LastSeenUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentApplicationUsers_LastSeenUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT UQ_BiAgentApplicationUsers_Tenant_Object
            UNIQUE (TenantId, EntraObjectId)
    );
END;

IF OBJECT_ID(N'dbo.BiAgentApplicationUserRoles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentApplicationUserRoles
    (
        UserId uniqueidentifier NOT NULL,
        RoleName nvarchar(64) NOT NULL,
        AssignedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentApplicationUserRoles_AssignedUtc DEFAULT (SYSUTCDATETIME()),
        AssignedByUserId uniqueidentifier NULL,
        CONSTRAINT PK_BiAgentApplicationUserRoles
            PRIMARY KEY (UserId, RoleName),
        CONSTRAINT CK_BiAgentApplicationUserRoles_RoleName
            CHECK (RoleName = N'ApplicationAdmin'),
        CONSTRAINT FK_BiAgentApplicationUserRoles_User
            FOREIGN KEY (UserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentApplicationUserRoles_AssignedBy
            FOREIGN KEY (AssignedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );
END;

IF OBJECT_ID(N'dbo.BiAgentApplicationRoleAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentApplicationRoleAudit
    (
        AuditId bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_BiAgentApplicationRoleAudit PRIMARY KEY,
        TargetUserId uniqueidentifier NOT NULL,
        RoleName nvarchar(64) NOT NULL,
        IsGranted bit NOT NULL,
        ChangedByUserId uniqueidentifier NOT NULL,
        ChangedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentApplicationRoleAudit_ChangedUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_BiAgentApplicationRoleAudit_Target
            FOREIGN KEY (TargetUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentApplicationRoleAudit_ChangedBy
            FOREIGN KEY (ChangedByUserId)
            REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );
END;

COMMIT TRANSACTION;
