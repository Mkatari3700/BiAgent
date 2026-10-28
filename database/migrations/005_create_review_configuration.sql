SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BiAgentReviewConfiguration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BiAgentReviewConfiguration
    (
        ConfigurationId tinyint NOT NULL
            CONSTRAINT PK_BiAgentReviewConfiguration PRIMARY KEY
            CONSTRAINT CK_BiAgentReviewConfiguration_Singleton CHECK (ConfigurationId = 1),
        Version int NOT NULL
            CONSTRAINT CK_BiAgentReviewConfiguration_Version CHECK (Version > 0),
        UpdatedUtc datetime2(3) NOT NULL,
        UpdatedBy nvarchar(256) NOT NULL,
        RowVersion rowversion NOT NULL
    );

    CREATE TABLE dbo.BiAgentReviewConfigurationAudit
    (
        AuditId bigint IDENTITY(1, 1) NOT NULL
            CONSTRAINT PK_BiAgentReviewConfigurationAudit PRIMARY KEY,
        Version int NOT NULL,
        ChangedUtc datetime2(3) NOT NULL
            CONSTRAINT DF_BiAgentReviewConfigurationAudit_ChangedUtc DEFAULT SYSUTCDATETIME(),
        ChangedBy nvarchar(256) NOT NULL
    );

    CREATE UNIQUE INDEX UX_BiAgentReviewConfigurationAudit_Version
        ON dbo.BiAgentReviewConfigurationAudit (Version);

    CREATE TABLE dbo.BiAgentReviewConfigurationSettings
    (
        ConfigurationId tinyint NOT NULL
            CONSTRAINT PK_BiAgentReviewConfigurationSettings PRIMARY KEY
            CONSTRAINT FK_BiAgentReviewConfigurationSettings_Configuration
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfiguration (ConfigurationId),
        TableNamePattern nvarchar(200) NOT NULL,
        ColumnNamePattern nvarchar(200) NOT NULL,
        MeasureNamePattern nvarchar(200) NOT NULL,
        PlaceholderColumnPattern nvarchar(200) NOT NULL,
        MeasureTableName nvarchar(200) NOT NULL,
        FactTablePrefix nvarchar(100) NOT NULL,
        DimensionTablePrefix nvarchar(100) NOT NULL,
        RequireDivideForDivision bit NOT NULL,
        WarnOnFormatInDax bit NOT NULL
    );

    CREATE TABLE dbo.BiAgentReviewConfigurationValues
    (
        ConfigurationId tinyint NOT NULL
            CONSTRAINT FK_BiAgentReviewConfigurationValues_Configuration
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfiguration (ConfigurationId),
        ValueType nvarchar(40) NOT NULL,
        Ordinal int NOT NULL,
        SettingValue nvarchar(200) NOT NULL,
        CONSTRAINT PK_BiAgentReviewConfigurationValues
            PRIMARY KEY (ConfigurationId, ValueType, Ordinal),
        CONSTRAINT CK_BiAgentReviewConfigurationValues_Type
            CHECK (ValueType IN (N'MeasureTableNameCandidate', N'GeneratedTablePrefix', N'RequiredMeasure')),
        CONSTRAINT CK_BiAgentReviewConfigurationValues_Ordinal CHECK (Ordinal >= 0)
    );

    CREATE TABLE dbo.BiAgentReviewRulePolicies
    (
        ConfigurationId tinyint NOT NULL
            CONSTRAINT FK_BiAgentReviewRulePolicies_Configuration
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfiguration (ConfigurationId),
        RuleKey nvarchar(100) NOT NULL,
        Enabled bit NOT NULL,
        Severity nvarchar(20) NOT NULL,
        CONSTRAINT PK_BiAgentReviewRulePolicies PRIMARY KEY (ConfigurationId, RuleKey),
        CONSTRAINT CK_BiAgentReviewRulePolicies_Severity
            CHECK (Severity IN (N'Error', N'Warning', N'Information'))
    );

    CREATE TABLE dbo.BiAgentModuleAvailability
    (
        ConfigurationId tinyint NOT NULL
            CONSTRAINT FK_BiAgentModuleAvailability_Configuration
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfiguration (ConfigurationId),
        ModuleKey nvarchar(100) NOT NULL,
        Enabled bit NOT NULL,
        CONSTRAINT PK_BiAgentModuleAvailability PRIMARY KEY (ConfigurationId, ModuleKey)
    );

    CREATE TABLE dbo.BiAgentReviewConfigurationAuditSettings
    (
        Version int NOT NULL
            CONSTRAINT PK_BiAgentReviewConfigurationAuditSettings PRIMARY KEY
            CONSTRAINT FK_BiAgentReviewConfigurationAuditSettings_Audit
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfigurationAudit (Version),
        TableNamePattern nvarchar(200) NOT NULL,
        ColumnNamePattern nvarchar(200) NOT NULL,
        MeasureNamePattern nvarchar(200) NOT NULL,
        PlaceholderColumnPattern nvarchar(200) NOT NULL,
        MeasureTableName nvarchar(200) NOT NULL,
        FactTablePrefix nvarchar(100) NOT NULL,
        DimensionTablePrefix nvarchar(100) NOT NULL,
        RequireDivideForDivision bit NOT NULL,
        WarnOnFormatInDax bit NOT NULL
    );

    CREATE TABLE dbo.BiAgentReviewConfigurationAuditValues
    (
        Version int NOT NULL
            CONSTRAINT FK_BiAgentReviewConfigurationAuditValues_Audit
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfigurationAudit (Version),
        ValueType nvarchar(40) NOT NULL,
        Ordinal int NOT NULL,
        SettingValue nvarchar(200) NOT NULL,
        CONSTRAINT PK_BiAgentReviewConfigurationAuditValues PRIMARY KEY (Version, ValueType, Ordinal),
        CONSTRAINT CK_BiAgentReviewConfigurationAuditValues_Type
            CHECK (ValueType IN (N'MeasureTableNameCandidate', N'GeneratedTablePrefix', N'RequiredMeasure')),
        CONSTRAINT CK_BiAgentReviewConfigurationAuditValues_Ordinal CHECK (Ordinal >= 0)
    );

    CREATE TABLE dbo.BiAgentReviewRulePolicyAudit
    (
        Version int NOT NULL
            CONSTRAINT FK_BiAgentReviewRulePolicyAudit_Audit
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfigurationAudit (Version),
        RuleKey nvarchar(100) NOT NULL,
        Enabled bit NOT NULL,
        Severity nvarchar(20) NOT NULL,
        CONSTRAINT PK_BiAgentReviewRulePolicyAudit PRIMARY KEY (Version, RuleKey),
        CONSTRAINT CK_BiAgentReviewRulePolicyAudit_Severity
            CHECK (Severity IN (N'Error', N'Warning', N'Information'))
    );

    CREATE TABLE dbo.BiAgentModuleAvailabilityAudit
    (
        Version int NOT NULL
            CONSTRAINT FK_BiAgentModuleAvailabilityAudit_Audit
                FOREIGN KEY REFERENCES dbo.BiAgentReviewConfigurationAudit (Version),
        ModuleKey nvarchar(100) NOT NULL,
        Enabled bit NOT NULL,
        CONSTRAINT PK_BiAgentModuleAvailabilityAudit PRIMARY KEY (Version, ModuleKey)
    );

    INSERT INTO dbo.BiAgentReviewConfiguration
        (ConfigurationId, Version, UpdatedUtc, UpdatedBy)
    VALUES
        (1, 1, SYSUTCDATETIME(), N'Migration 005 default');

    INSERT INTO dbo.BiAgentReviewConfigurationSettings
    (
        ConfigurationId, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
        PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
        RequireDivideForDivision, WarnOnFormatInDax
    )
    VALUES
    (
        1,
        N'^[A-Z][A-Za-z0-9]*$',
        N'^[A-Z][A-Za-z0-9]*$',
        N'^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)*$',
        N'^Column\d*$',
        N'Measures',
        N'Fact',
        N'Dim',
        1,
        1
    );

    INSERT INTO dbo.BiAgentReviewConfigurationValues (ConfigurationId, ValueType, Ordinal, SettingValue)
    VALUES
        (1, N'MeasureTableNameCandidate', 0, N'measures (2)'),
        (1, N'GeneratedTablePrefix', 0, N'LocalDateTable_'),
        (1, N'GeneratedTablePrefix', 1, N'DateTableTemplate_');

    INSERT INTO dbo.BiAgentReviewRulePolicies (ConfigurationId, RuleKey, Enabled, Severity)
    VALUES
        (1, N'project-structure', 1, N'Error'),
        (1, N'table-naming', 1, N'Warning'),
        (1, N'column-naming', 1, N'Warning'),
        (1, N'placeholder-column', 1, N'Warning'),
        (1, N'measure-table-name', 1, N'Warning'),
        (1, N'measure-naming', 1, N'Warning'),
        (1, N'required-measures', 1, N'Error'),
        (1, N'relationship-columns', 1, N'Error'),
        (1, N'star-schema-relationships', 1, N'Warning'),
        (1, N'single-direction-relationships', 1, N'Warning'),
        (1, N'explicit-measures', 1, N'Warning'),
        (1, N'dax-division', 1, N'Warning'),
        (1, N'dax-format', 1, N'Warning');

    INSERT INTO dbo.BiAgentModuleAvailability (ConfigurationId, ModuleKey, Enabled)
    VALUES
        (1, N'reviews', 1),
        (1, N'governanceAudit', 1),
        (1, N'performanceOptimization', 0),
        (1, N'releaseReadiness', 0),
        (1, N'documentationGenerator', 0),
        (1, N'architectureReview', 0),
        (1, N'biDeliveryManager', 0);

    INSERT INTO dbo.BiAgentReviewConfigurationAudit (Version, ChangedBy)
    VALUES (1, N'Migration 005 default');

    INSERT INTO dbo.BiAgentReviewConfigurationAuditSettings
    (
        Version, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
        PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
        RequireDivideForDivision, WarnOnFormatInDax
    )
    SELECT
        1, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
        PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
        RequireDivideForDivision, WarnOnFormatInDax
    FROM dbo.BiAgentReviewConfigurationSettings
    WHERE ConfigurationId = 1;

    INSERT INTO dbo.BiAgentReviewConfigurationAuditValues (Version, ValueType, Ordinal, SettingValue)
    SELECT 1, ValueType, Ordinal, SettingValue
    FROM dbo.BiAgentReviewConfigurationValues
    WHERE ConfigurationId = 1;

    INSERT INTO dbo.BiAgentReviewRulePolicyAudit (Version, RuleKey, Enabled, Severity)
    SELECT 1, RuleKey, Enabled, Severity
    FROM dbo.BiAgentReviewRulePolicies
    WHERE ConfigurationId = 1;

    INSERT INTO dbo.BiAgentModuleAvailabilityAudit (Version, ModuleKey, Enabled)
    SELECT 1, ModuleKey, Enabled
    FROM dbo.BiAgentModuleAvailability
    WHERE ConfigurationId = 1;
END;

COMMIT TRANSACTION;
