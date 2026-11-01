SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentSupportRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentSupportRequests
    (
        SupportRequestId uniqueidentifier NOT NULL
            CONSTRAINT PK_BiAgentSupportRequests PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        Category nvarchar(40) NOT NULL,
        Subject nvarchar(200) NOT NULL,
        Message nvarchar(4000) NOT NULL,
        Status nvarchar(20) NOT NULL
            CONSTRAINT DF_BiAgentSupportRequests_Status DEFAULT N'Open',
        CreatedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentSupportRequests_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc datetimeoffset(3) NOT NULL
            CONSTRAINT DF_BiAgentSupportRequests_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        AdminResponse nvarchar(4000) NULL,
        RespondedByUserId uniqueidentifier NULL,
        RespondedUtc datetimeoffset(3) NULL,
        CONSTRAINT CK_BiAgentSupportRequests_Category
            CHECK (Category IN (N'Technical issue', N'Access request', N'Feature request', N'Other')),
        CONSTRAINT CK_BiAgentSupportRequests_Status
            CHECK (Status IN (N'Open', N'In progress', N'Resolved')),
        CONSTRAINT FK_BiAgentSupportRequests_User
            FOREIGN KEY (UserId) REFERENCES dbo.BiAgentApplicationUsers (UserId),
        CONSTRAINT FK_BiAgentSupportRequests_RespondedBy
            FOREIGN KEY (RespondedByUserId) REFERENCES dbo.BiAgentApplicationUsers (UserId)
    );

    CREATE INDEX IX_BiAgentSupportRequests_User_CreatedUtc
        ON dbo.BiAgentSupportRequests (UserId, CreatedUtc DESC);
    CREATE INDEX IX_BiAgentSupportRequests_Status_CreatedUtc
        ON dbo.BiAgentSupportRequests (Status, CreatedUtc DESC);
END;

COMMIT TRANSACTION;
