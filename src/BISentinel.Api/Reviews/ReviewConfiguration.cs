using System.Text.RegularExpressions;

namespace BISentinel.Api.Reviews;

/// <summary>Global review standards and policy settings persisted in SQL.</summary>
public sealed record ReviewConfiguration
{
    /// <summary>Regular expression used to validate table names.</summary>
    public string TableNamePattern { get; init; } = "^[A-Z][A-Za-z0-9]*$";

    /// <summary>Regular expression used to validate column names.</summary>
    public string ColumnNamePattern { get; init; } = "^[A-Z][A-Za-z0-9]*$";

    /// <summary>Regular expression used to validate measure names.</summary>
    public string MeasureNamePattern { get; init; } = "^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)*$";

    /// <summary>Regular expression that identifies placeholder column names.</summary>
    public string PlaceholderColumnPattern { get; init; } = "^Column\\d*$";

    /// <summary>Expected name for a dedicated measure container.</summary>
    public string MeasureTableName { get; init; } = "Measures";

    /// <summary>Known table names treated as possible measure containers.</summary>
    public string[] MeasureTableNameCandidates { get; init; } = [];

    /// <summary>Prefix used to identify fact tables.</summary>
    public string FactTablePrefix { get; init; } = "Fact";

    /// <summary>Prefix used to identify dimension tables.</summary>
    public string DimensionTablePrefix { get; init; } = "Dim";

    /// <summary>Prefixes used to exclude generated tables from selected checks.</summary>
    public string[] GeneratedTablePrefixes { get; init; } = ["LocalDateTable_", "DateTableTemplate_"];

    /// <summary>Measure names that every reviewed semantic model should contain.</summary>
    public string[] RequiredMeasures { get; init; } = [];

    /// <summary>Whether direct division in DAX should be flagged.</summary>
    public bool RequireDivideForDivision { get; init; } = true;

    /// <summary>Whether FORMAT calls in DAX should be flagged.</summary>
    public bool WarnOnFormatInDax { get; init; } = true;

    /// <summary>Enabled state and severity for each registered review rule.</summary>
    public Dictionary<string, ReviewRulePolicy> Rules { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Database-controlled availability of the application modules.</summary>
    public ModuleAvailability Modules { get; init; } = new();

    public static ReviewConfiguration FromReviewOptions(ReviewOptions options) => new()
    {
        TableNamePattern = options.TableNamePattern,
        ColumnNamePattern = options.ColumnNamePattern,
        MeasureNamePattern = options.MeasureNamePattern,
        PlaceholderColumnPattern = options.PlaceholderColumnPattern,
        MeasureTableName = options.MeasureTableName,
        MeasureTableNameCandidates = options.MeasureTableNameCandidates,
        FactTablePrefix = options.FactTablePrefix,
        DimensionTablePrefix = options.DimensionTablePrefix,
        GeneratedTablePrefixes = options.GeneratedTablePrefixes,
        RequiredMeasures = options.RequiredMeasures,
        RequireDivideForDivision = options.RequireDivideForDivision,
        WarnOnFormatInDax = options.WarnOnFormatInDax,
        Rules = new Dictionary<string, ReviewRulePolicy>(options.Rules, StringComparer.OrdinalIgnoreCase)
    };

    public ReviewOptions ToReviewOptions(ReviewOptions hostOptions) => new()
    {
        PbiToolsPath = hostOptions.PbiToolsPath,
        ProjectId = hostOptions.ProjectId,
        TableNamePattern = TableNamePattern,
        ColumnNamePattern = ColumnNamePattern,
        MeasureNamePattern = MeasureNamePattern,
        PlaceholderColumnPattern = PlaceholderColumnPattern,
        MeasureTableName = MeasureTableName,
        MeasureTableNameCandidates = MeasureTableNameCandidates,
        FactTablePrefix = FactTablePrefix,
        DimensionTablePrefix = DimensionTablePrefix,
        GeneratedTablePrefixes = GeneratedTablePrefixes,
        RequiredMeasures = RequiredMeasures,
        RequireDivideForDivision = RequireDivideForDivision,
        WarnOnFormatInDax = WarnOnFormatInDax,
        Rules = new Dictionary<string, ReviewRulePolicy>(Rules, StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>Controls which module workspaces are available to users.</summary>
public sealed record ModuleAvailability
{
    public bool Reviews { get; init; } = true;

    public bool GovernanceAudit { get; init; } = true;

    public bool PerformanceOptimization { get; init; }

    public bool ReleaseReadiness { get; init; }

    public bool DocumentationGenerator { get; init; }

    public bool ArchitectureReview { get; init; }

    public bool BiDeliveryManager { get; init; }
}

public sealed record ReviewConfigurationSnapshot(
    ReviewConfiguration Configuration,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy);

/// <summary>Request to save the global review configuration using optimistic concurrency.</summary>
public sealed record UpdateReviewConfigurationRequest
{
    /// <summary>Version returned by the last read; rejects overwriting a newer edit.</summary>
    public required int ExpectedVersion { get; init; }

    /// <summary>New global review rules and standards.</summary>
    public required ReviewConfiguration Configuration { get; init; }
}

/// <summary>Global review configuration together with its database version metadata.</summary>
public sealed record ReviewConfigurationResponse(
    ReviewConfiguration Configuration,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy);

public static class ReviewConfigurationValidator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static Dictionary<string, string[]> Validate(
        ReviewConfiguration configuration,
        IEnumerable<string> supportedRules)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (configuration is null)
        {
            errors[nameof(configuration)] = ["Review configuration is required."];
            return errors;
        }

        ValidateRegex(nameof(configuration.TableNamePattern), configuration.TableNamePattern, errors);
        ValidateRegex(nameof(configuration.ColumnNamePattern), configuration.ColumnNamePattern, errors);
        ValidateRegex(nameof(configuration.MeasureNamePattern), configuration.MeasureNamePattern, errors);
        ValidateRegex(nameof(configuration.PlaceholderColumnPattern), configuration.PlaceholderColumnPattern, errors);
        ValidateText(nameof(configuration.MeasureTableName), configuration.MeasureTableName, 200, errors);
        ValidateText(nameof(configuration.FactTablePrefix), configuration.FactTablePrefix, 100, errors);
        ValidateText(nameof(configuration.DimensionTablePrefix), configuration.DimensionTablePrefix, 100, errors);
        ValidateList(nameof(configuration.MeasureTableNameCandidates), configuration.MeasureTableNameCandidates, errors);
        ValidateList(nameof(configuration.GeneratedTablePrefixes), configuration.GeneratedTablePrefixes, errors);
        ValidateList(nameof(configuration.RequiredMeasures), configuration.RequiredMeasures, errors);
        if (configuration.Modules is null)
        {
            errors[nameof(configuration.Modules)] = ["Module availability settings are required."];
        }

        var allowedRules = supportedRules.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (configuration.Rules is null)
        {
            errors[nameof(configuration.Rules)] = ["Rule settings are required."];
            return errors;
        }

        foreach (var rule in configuration.Rules)
        {
            if (!allowedRules.Contains(rule.Key))
            {
                errors[$"Rules.{rule.Key}"] = ["This rule is not registered by the application."];
            }
            else if (rule.Value is null)
            {
                errors[$"Rules.{rule.Key}"] = ["A policy is required for each configured rule."];
            }
            else if (rule.Value.Severity is not ("Error" or "Warning" or "Information"))
            {
                errors[$"Rules.{rule.Key}.Severity"] = ["Severity must be Error, Warning, or Information."];
            }
        }

        var missingRules = allowedRules.Except(
            configuration.Rules.Keys,
            StringComparer.OrdinalIgnoreCase).ToArray();
        if (missingRules.Length > 0)
        {
            errors[nameof(configuration.Rules)] =
                [$"Include a policy for every registered rule: {string.Join(", ", missingRules)}."];
        }

        return errors;
    }

    private static void ValidateRegex(string name, string? pattern, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 200)
        {
            errors[name] = ["A regular expression is required and must be 200 characters or fewer."];
            return;
        }

        try
        {
            _ = new Regex(
                pattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
        }
        catch (ArgumentException)
        {
            errors[name] = ["Enter a valid regular expression."];
        }
    }

    private static void ValidateText(string name, string? value, int maximumLength, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
        {
            errors[name] = [$"A value is required and must be {maximumLength} characters or fewer."];
        }
    }

    private static void ValidateList(string name, string[]? values, Dictionary<string, string[]> errors)
    {
        if (values is null || values.Length > 100 || values.Any(value =>
                string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200))
        {
            errors[name] = ["Provide at most 100 non-empty values, each 200 characters or fewer."];
        }
    }
}
