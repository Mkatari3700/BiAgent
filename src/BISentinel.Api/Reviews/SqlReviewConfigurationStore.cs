using System.Data;
using Microsoft.Data.SqlClient;

namespace BISentinel.Api.Reviews;

public sealed class SqlReviewConfigurationStore(string connectionString) : IReviewConfigurationStore
{
    public async Task<ReviewConfigurationSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var version = 0;
        var updatedUtc = default(DateTime);
        var updatedBy = string.Empty;
        await using (var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT Version, UpdatedUtc, UpdatedBy
            FROM dbo.BiAgentReviewConfiguration
            WHERE ConfigurationId = 1;
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The global review configuration row is missing.");
            }

            version = reader.GetInt32(0);
            updatedUtc = reader.GetDateTime(1);
            updatedBy = reader.GetString(2);
        }

        ReviewConfiguration configuration;
        await using (var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT TableNamePattern, ColumnNamePattern, MeasureNamePattern, PlaceholderColumnPattern,
                   MeasureTableName, FactTablePrefix, DimensionTablePrefix,
                   RequireDivideForDivision, WarnOnFormatInDax
            FROM dbo.BiAgentReviewConfigurationSettings
            WHERE ConfigurationId = 1;
            """))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidDataException("The global review configuration settings are missing.");
            }

            configuration = new ReviewConfiguration
            {
                TableNamePattern = reader.GetString(0),
                ColumnNamePattern = reader.GetString(1),
                MeasureNamePattern = reader.GetString(2),
                PlaceholderColumnPattern = reader.GetString(3),
                MeasureTableName = reader.GetString(4),
                FactTablePrefix = reader.GetString(5),
                DimensionTablePrefix = reader.GetString(6),
                RequireDivideForDivision = reader.GetBoolean(7),
                WarnOnFormatInDax = reader.GetBoolean(8)
            };
        }

        var values = await ReadValuesAsync(connection, transaction, cancellationToken);
        var rules = await ReadRulesAsync(connection, transaction, cancellationToken);
        var modules = await ReadModulesAsync(connection, transaction, cancellationToken);
        configuration = configuration with
        {
            MeasureTableNameCandidates = GetValues(values, "MeasureTableNameCandidate"),
            GeneratedTablePrefixes = GetValues(values, "GeneratedTablePrefix"),
            RequiredMeasures = GetValues(values, "RequiredMeasure"),
            Rules = rules,
            Modules = modules
        };

        await transaction.CommitAsync(cancellationToken);
        return new ReviewConfigurationSnapshot(
            configuration,
            version,
            new DateTimeOffset(DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc)),
            updatedBy);
    }

    public async Task<ReviewConfigurationSnapshot?> UpdateAsync(
        ReviewConfiguration configuration,
        int expectedVersion,
        string changedBy,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var version = 0;
        var updatedUtc = default(DateTime);
        var updatedBy = string.Empty;
        await using (var update = CreateCommand(
            connection,
            transaction,
            """
            UPDATE dbo.BiAgentReviewConfiguration
            SET Version = Version + 1,
                UpdatedUtc = SYSUTCDATETIME(),
                UpdatedBy = @ChangedBy
            OUTPUT INSERTED.Version, INSERTED.UpdatedUtc, INSERTED.UpdatedBy
            WHERE ConfigurationId = 1 AND Version = @ExpectedVersion;
            """))
        {
            update.Parameters.Add("@ChangedBy", SqlDbType.NVarChar, 256).Value = changedBy;
            update.Parameters.Add("@ExpectedVersion", SqlDbType.Int).Value = expectedVersion;

            await using (var reader = await update.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    version = reader.GetInt32(0);
                    updatedUtc = reader.GetDateTime(1);
                    updatedBy = reader.GetString(2);
                }
            }

            if (version == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }
        }

        await using (var updateSettings = CreateCommand(
            connection,
            transaction,
            """
            UPDATE dbo.BiAgentReviewConfigurationSettings
            SET TableNamePattern = @TableNamePattern,
                ColumnNamePattern = @ColumnNamePattern,
                MeasureNamePattern = @MeasureNamePattern,
                PlaceholderColumnPattern = @PlaceholderColumnPattern,
                MeasureTableName = @MeasureTableName,
                FactTablePrefix = @FactTablePrefix,
                DimensionTablePrefix = @DimensionTablePrefix,
                RequireDivideForDivision = @RequireDivideForDivision,
                WarnOnFormatInDax = @WarnOnFormatInDax
            WHERE ConfigurationId = 1;
            """))
        {
            AddSettingsParameters(updateSettings, configuration);
            if (await updateSettings.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidDataException("The global review configuration settings are missing.");
            }
        }

        await using (var deleteChildren = CreateCommand(
            connection,
            transaction,
            """
            DELETE FROM dbo.BiAgentReviewConfigurationValues WHERE ConfigurationId = 1;
            DELETE FROM dbo.BiAgentReviewRulePolicies WHERE ConfigurationId = 1;
            DELETE FROM dbo.BiAgentModuleAvailability WHERE ConfigurationId = 1;
            """))
        {
            await deleteChildren.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertValuesAsync(connection, transaction, configuration, cancellationToken);
        await InsertRulesAsync(connection, transaction, configuration, cancellationToken);
        await InsertModulesAsync(connection, transaction, configuration, cancellationToken);

        await using (var insertAudit = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO dbo.BiAgentReviewConfigurationAudit (Version, ChangedUtc, ChangedBy)
            VALUES (@Version, @ChangedUtc, @ChangedBy);

            INSERT INTO dbo.BiAgentReviewConfigurationAuditSettings
            (
                Version, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
                PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
                RequireDivideForDivision, WarnOnFormatInDax
            )
            SELECT
                @Version, TableNamePattern, ColumnNamePattern, MeasureNamePattern,
                PlaceholderColumnPattern, MeasureTableName, FactTablePrefix, DimensionTablePrefix,
                RequireDivideForDivision, WarnOnFormatInDax
            FROM dbo.BiAgentReviewConfigurationSettings
            WHERE ConfigurationId = 1;

            INSERT INTO dbo.BiAgentReviewConfigurationAuditValues (Version, ValueType, Ordinal, SettingValue)
            SELECT @Version, ValueType, Ordinal, SettingValue
            FROM dbo.BiAgentReviewConfigurationValues
            WHERE ConfigurationId = 1;

            INSERT INTO dbo.BiAgentReviewRulePolicyAudit (Version, RuleKey, Enabled, Severity)
            SELECT @Version, RuleKey, Enabled, Severity
            FROM dbo.BiAgentReviewRulePolicies
            WHERE ConfigurationId = 1;

            INSERT INTO dbo.BiAgentModuleAvailabilityAudit (Version, ModuleKey, Enabled)
            SELECT @Version, ModuleKey, Enabled
            FROM dbo.BiAgentModuleAvailability
            WHERE ConfigurationId = 1;
            """))
        {
            insertAudit.Parameters.Add("@Version", SqlDbType.Int).Value = version;
            insertAudit.Parameters.Add("@ChangedUtc", SqlDbType.DateTime2).Value = updatedUtc;
            insertAudit.Parameters.Add("@ChangedBy", SqlDbType.NVarChar, 256).Value = updatedBy;
            await insertAudit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new ReviewConfigurationSnapshot(
            configuration,
            version,
            new DateTimeOffset(DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc)),
            updatedBy);
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static async Task<Dictionary<string, List<(int Ordinal, string Value)>>> ReadValuesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, List<(int Ordinal, string Value)>>(StringComparer.Ordinal);
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT ValueType, Ordinal, SettingValue
            FROM dbo.BiAgentReviewConfigurationValues
            WHERE ConfigurationId = 1
            ORDER BY ValueType, Ordinal;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var valueType = reader.GetString(0);
            if (!values.TryGetValue(valueType, out var list))
            {
                list = [];
                values.Add(valueType, list);
            }

            list.Add((reader.GetInt32(1), reader.GetString(2)));
        }

        foreach (var list in values.Values)
        {
            if (list.Where((value, index) => value.Ordinal != index).Any())
            {
                throw new InvalidDataException("Review configuration list ordinals must be sequential.");
            }
        }

        return values;
    }

    private static async Task<Dictionary<string, ReviewRulePolicy>> ReadRulesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rules = new Dictionary<string, ReviewRulePolicy>(StringComparer.OrdinalIgnoreCase);
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT RuleKey, Enabled, Severity
            FROM dbo.BiAgentReviewRulePolicies
            WHERE ConfigurationId = 1;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(reader.GetString(0), new ReviewRulePolicy
            {
                Enabled = reader.GetBoolean(1),
                Severity = reader.GetString(2)
            });
        }

        return rules;
    }

    private static async Task<ModuleAvailability> ReadModulesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var modules = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT ModuleKey, Enabled
            FROM dbo.BiAgentModuleAvailability
            WHERE ConfigurationId = 1;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            modules.Add(reader.GetString(0), reader.GetBoolean(1));
        }

        var knownModuleKeys = new HashSet<string>(
            ["reviews", "governanceAudit", "performanceOptimization", "releaseReadiness",
             "documentationGenerator", "architectureReview", "biDeliveryManager"],
            StringComparer.Ordinal);
        if (!modules.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(knownModuleKeys))
        {
            throw new InvalidDataException("The database module availability rows are incomplete or invalid.");
        }

        return new ModuleAvailability
        {
            Reviews = modules["reviews"],
            GovernanceAudit = modules["governanceAudit"],
            PerformanceOptimization = modules["performanceOptimization"],
            ReleaseReadiness = modules["releaseReadiness"],
            DocumentationGenerator = modules["documentationGenerator"],
            ArchitectureReview = modules["architectureReview"],
            BiDeliveryManager = modules["biDeliveryManager"]
        };
    }

    private static string[] GetValues(
        IReadOnlyDictionary<string, List<(int Ordinal, string Value)>> values,
        string valueType) =>
        values.TryGetValue(valueType, out var list)
            ? list.Select(value => value.Value).ToArray()
            : [];

    private static void AddSettingsParameters(SqlCommand command, ReviewConfiguration configuration)
    {
        command.Parameters.Add("@TableNamePattern", SqlDbType.NVarChar, 200).Value = configuration.TableNamePattern;
        command.Parameters.Add("@ColumnNamePattern", SqlDbType.NVarChar, 200).Value = configuration.ColumnNamePattern;
        command.Parameters.Add("@MeasureNamePattern", SqlDbType.NVarChar, 200).Value = configuration.MeasureNamePattern;
        command.Parameters.Add("@PlaceholderColumnPattern", SqlDbType.NVarChar, 200).Value =
            configuration.PlaceholderColumnPattern;
        command.Parameters.Add("@MeasureTableName", SqlDbType.NVarChar, 200).Value = configuration.MeasureTableName;
        command.Parameters.Add("@FactTablePrefix", SqlDbType.NVarChar, 100).Value = configuration.FactTablePrefix;
        command.Parameters.Add("@DimensionTablePrefix", SqlDbType.NVarChar, 100).Value =
            configuration.DimensionTablePrefix;
        command.Parameters.Add("@RequireDivideForDivision", SqlDbType.Bit).Value =
            configuration.RequireDivideForDivision;
        command.Parameters.Add("@WarnOnFormatInDax", SqlDbType.Bit).Value = configuration.WarnOnFormatInDax;
    }

    private static async Task InsertValuesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ReviewConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO dbo.BiAgentReviewConfigurationValues (ConfigurationId, ValueType, Ordinal, SettingValue)
            VALUES (1, @ValueType, @Ordinal, @SettingValue);
            """);
        var valueType = command.Parameters.Add("@ValueType", SqlDbType.NVarChar, 40);
        var ordinal = command.Parameters.Add("@Ordinal", SqlDbType.Int);
        var value = command.Parameters.Add("@SettingValue", SqlDbType.NVarChar, 200);

        await InsertListAsync(configuration.MeasureTableNameCandidates, "MeasureTableNameCandidate");
        await InsertListAsync(configuration.GeneratedTablePrefixes, "GeneratedTablePrefix");
        await InsertListAsync(configuration.RequiredMeasures, "RequiredMeasure");

        async Task InsertListAsync(IEnumerable<string> entries, string type)
        {
            valueType.Value = type;
            var index = 0;
            foreach (var entry in entries)
            {
                ordinal.Value = index++;
                value.Value = entry;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task InsertRulesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ReviewConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO dbo.BiAgentReviewRulePolicies (ConfigurationId, RuleKey, Enabled, Severity)
            VALUES (1, @RuleKey, @Enabled, @Severity);
            """);
        var key = command.Parameters.Add("@RuleKey", SqlDbType.NVarChar, 100);
        var enabled = command.Parameters.Add("@Enabled", SqlDbType.Bit);
        var severity = command.Parameters.Add("@Severity", SqlDbType.NVarChar, 20);
        foreach (var (ruleKey, policy) in configuration.Rules)
        {
            key.Value = ruleKey;
            enabled.Value = policy.Enabled;
            severity.Value = policy.Severity;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertModulesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ReviewConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            INSERT INTO dbo.BiAgentModuleAvailability (ConfigurationId, ModuleKey, Enabled)
            VALUES (1, @ModuleKey, @Enabled);
            """);
        var moduleKey = command.Parameters.Add("@ModuleKey", SqlDbType.NVarChar, 100);
        var enabled = command.Parameters.Add("@Enabled", SqlDbType.Bit);
        foreach (var (key, value) in GetModuleValues(configuration.Modules))
        {
            moduleKey.Value = key;
            enabled.Value = value;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IEnumerable<(string Key, bool Value)> GetModuleValues(ModuleAvailability modules)
    {
        yield return ("reviews", modules.Reviews);
        yield return ("governanceAudit", modules.GovernanceAudit);
        yield return ("performanceOptimization", modules.PerformanceOptimization);
        yield return ("releaseReadiness", modules.ReleaseReadiness);
        yield return ("documentationGenerator", modules.DocumentationGenerator);
        yield return ("architectureReview", modules.ArchitectureReview);
        yield return ("biDeliveryManager", modules.BiDeliveryManager);
    }
}
