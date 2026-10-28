SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.BiAgentReviewConfiguration', N'U') IS NULL
BEGIN
    ;THROW 51000, 'Apply migration 005 before normalizing review configuration.', 1;
END;

IF COL_LENGTH(N'dbo.BiAgentReviewConfiguration', N'ConfigurationJson') IS NULL
BEGIN
    RETURN;
END;

BEGIN TRANSACTION;

DECLARE @Snapshots TABLE
(
    Version int NOT NULL PRIMARY KEY,
    ConfigurationJson nvarchar(max) NOT NULL,
    ChangedUtc datetime2(3) NOT NULL,
    ChangedBy nvarchar(256) NOT NULL
);

INSERT INTO @Snapshots (Version, ConfigurationJson, ChangedUtc, ChangedBy)
SELECT Version, ConfigurationJson, ChangedUtc, ChangedBy
FROM dbo.BiAgentReviewConfigurationAudit;

INSERT INTO @Snapshots (Version, ConfigurationJson, ChangedUtc, ChangedBy)
SELECT configuration.Version, configuration.ConfigurationJson, configuration.UpdatedUtc, configuration.UpdatedBy
FROM dbo.BiAgentReviewConfiguration AS configuration
WHERE configuration.ConfigurationId = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM @Snapshots AS snapshot
      WHERE snapshot.Version = configuration.Version
  );

DECLARE @CurrentJson nvarchar(max);
SELECT @CurrentJson = ConfigurationJson
FROM dbo.BiAgentReviewConfiguration
WHERE ConfigurationId = 1;

ALTER TABLE dbo.BiAgentReviewConfiguration
    DROP CONSTRAINT CK_BiAgentReviewConfiguration_Json;
ALTER TABLE dbo.BiAgentReviewConfigurationAudit
    DROP CONSTRAINT CK_BiAgentReviewConfigurationAudit_Json;

ALTER TABLE dbo.BiAgentReviewConfiguration
    DROP COLUMN ConfigurationJson;
ALTER TABLE dbo.BiAgentReviewConfigurationAudit
    DROP COLUMN ConfigurationJson;

INSERT INTO dbo.BiAgentReviewConfigurationAudit (Version, ChangedUtc, ChangedBy)
SELECT snapshot.Version, snapshot.ChangedUtc, snapshot.ChangedBy
FROM @Snapshots AS snapshot
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.BiAgentReviewConfigurationAudit AS audit
    WHERE audit.Version = snapshot.Version
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

INSERT INTO dbo.BiAgentReviewConfigurationSettings
(
    ConfigurationId, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
    PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
    RequireDivideForDivision, WarnOnFormatInDax
)
SELECT
    1,
    COALESCE(JSON_VALUE(@CurrentJson, '$.tableNamePattern'), N'^[A-Z][A-Za-z0-9]*$'),
    COALESCE(JSON_VALUE(@CurrentJson, '$.columnNamePattern'), N'^[A-Z][A-Za-z0-9]*$'),
    COALESCE(JSON_VALUE(@CurrentJson, '$.measureNamePattern'), N'^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)*$'),
    COALESCE(JSON_VALUE(@CurrentJson, '$.placeholderColumnPattern'), N'^Column\d*$'),
    COALESCE(JSON_VALUE(@CurrentJson, '$.measureTableName'), N'Measures'),
    COALESCE(JSON_VALUE(@CurrentJson, '$.factTablePrefix'), N'Fact'),
    COALESCE(JSON_VALUE(@CurrentJson, '$.dimensionTablePrefix'), N'Dim'),
    COALESCE(TRY_CONVERT(bit, JSON_VALUE(@CurrentJson, '$.requireDivideForDivision')), 1),
    COALESCE(TRY_CONVERT(bit, JSON_VALUE(@CurrentJson, '$.warnOnFormatInDax')), 1);

INSERT INTO dbo.BiAgentReviewConfigurationValues (ConfigurationId, ValueType, Ordinal, SettingValue)
SELECT 1, source.ValueType, CONVERT(int, item.[key]), item.value
FROM
(
    VALUES
        (N'MeasureTableNameCandidate', N'$.measureTableNameCandidates'),
        (N'GeneratedTablePrefix', N'$.generatedTablePrefixes'),
        (N'RequiredMeasure', N'$.requiredMeasures')
) AS source (ValueType, JsonPath)
CROSS APPLY OPENJSON(@CurrentJson, source.JsonPath) AS item;

INSERT INTO dbo.BiAgentReviewRulePolicies (ConfigurationId, RuleKey, Enabled, Severity)
SELECT
    1,
    policy.[key],
    COALESCE(details.Enabled, 1),
    COALESCE(details.Severity, N'Warning')
FROM OPENJSON(@CurrentJson, '$.rules') AS policy
CROSS APPLY OPENJSON(policy.value)
WITH (Enabled bit '$.enabled', Severity nvarchar(20) '$.severity') AS details;

INSERT INTO dbo.BiAgentModuleAvailability (ConfigurationId, ModuleKey, Enabled)
SELECT
    1,
    module.ModuleKey,
    COALESCE(TRY_CONVERT(bit, configuredModule.value), module.DefaultEnabled)
FROM
(
    VALUES
        (N'reviews', CONVERT(bit, 1)),
        (N'governanceAudit', CONVERT(bit, 1)),
        (N'performanceOptimization', CONVERT(bit, 0)),
        (N'releaseReadiness', CONVERT(bit, 0)),
        (N'documentationGenerator', CONVERT(bit, 0)),
        (N'architectureReview', CONVERT(bit, 0)),
        (N'biDeliveryManager', CONVERT(bit, 0))
) AS module (ModuleKey, DefaultEnabled)
LEFT JOIN OPENJSON(@CurrentJson, '$.modules') AS configuredModule
    ON configuredModule.[key] = module.ModuleKey;

INSERT INTO dbo.BiAgentReviewConfigurationAuditSettings
(
    Version, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
    PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
    RequireDivideForDivision, WarnOnFormatInDax
)
SELECT
    snapshot.Version,
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.tableNamePattern'), N'^[A-Z][A-Za-z0-9]*$'),
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.columnNamePattern'), N'^[A-Z][A-Za-z0-9]*$'),
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.measureNamePattern'), N'^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)*$'),
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.placeholderColumnPattern'), N'^Column\d*$'),
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.measureTableName'), N'Measures'),
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.factTablePrefix'), N'Fact'),
    COALESCE(JSON_VALUE(snapshot.ConfigurationJson, '$.dimensionTablePrefix'), N'Dim'),
    COALESCE(TRY_CONVERT(bit, JSON_VALUE(snapshot.ConfigurationJson, '$.requireDivideForDivision')), 1),
    COALESCE(TRY_CONVERT(bit, JSON_VALUE(snapshot.ConfigurationJson, '$.warnOnFormatInDax')), 1)
FROM @Snapshots AS snapshot;

INSERT INTO dbo.BiAgentReviewConfigurationAuditValues (Version, ValueType, Ordinal, SettingValue)
SELECT snapshot.Version, source.ValueType, CONVERT(int, item.[key]), item.value
FROM @Snapshots AS snapshot
CROSS APPLY
(
    VALUES
        (N'MeasureTableNameCandidate', N'$.measureTableNameCandidates'),
        (N'GeneratedTablePrefix', N'$.generatedTablePrefixes'),
        (N'RequiredMeasure', N'$.requiredMeasures')
) AS source (ValueType, JsonPath)
CROSS APPLY OPENJSON(snapshot.ConfigurationJson, source.JsonPath) AS item;

INSERT INTO dbo.BiAgentReviewRulePolicyAudit (Version, RuleKey, Enabled, Severity)
SELECT
    snapshot.Version,
    policy.[key],
    COALESCE(details.Enabled, 1),
    COALESCE(details.Severity, N'Warning')
FROM @Snapshots AS snapshot
CROSS APPLY OPENJSON(snapshot.ConfigurationJson, '$.rules') AS policy
CROSS APPLY OPENJSON(policy.value)
WITH (Enabled bit '$.enabled', Severity nvarchar(20) '$.severity') AS details;

INSERT INTO dbo.BiAgentModuleAvailabilityAudit (Version, ModuleKey, Enabled)
SELECT
    snapshot.Version,
    module.ModuleKey,
    COALESCE(
        TRY_CONVERT(bit, configuredModule.value),
        module.DefaultEnabled)
FROM @Snapshots AS snapshot
CROSS JOIN
(
    VALUES
        (N'reviews', CONVERT(bit, 1)),
        (N'governanceAudit', CONVERT(bit, 1)),
        (N'performanceOptimization', CONVERT(bit, 0)),
        (N'releaseReadiness', CONVERT(bit, 0)),
        (N'documentationGenerator', CONVERT(bit, 0)),
        (N'architectureReview', CONVERT(bit, 0)),
        (N'biDeliveryManager', CONVERT(bit, 0))
) AS module (ModuleKey, DefaultEnabled)
OUTER APPLY
(
    SELECT setting.value
    FROM OPENJSON(snapshot.ConfigurationJson, '$.modules') AS setting
    WHERE setting.[key] = module.ModuleKey
) AS configuredModule;

COMMIT TRANSACTION;
