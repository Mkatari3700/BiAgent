using System.Text.RegularExpressions;

namespace BISentinel.Api.Reviews;

public sealed class ProjectStructureRule : IReviewRule
{
    public string Key => "project-structure";

    public bool RequiresSemanticModel => false;

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var missingFiles = project.RequiredFiles.Where(file => !File.Exists(file)).ToArray();
        var findings = missingFiles.Select(file => new PbipReviewFinding(
            "PBIP-STRUCT-001",
            string.Empty,
            $"Required project file '{Path.GetFileName(file)}' is missing.",
            Path.GetFileName(file),
            "Restore the missing PBIP file or regenerate the project from Power BI Desktop.")).ToArray();

        return new ReviewRuleResult(project.RequiredFiles.Count, findings);
    }
}

public sealed class TableNamingRule : IReviewRule
{
    public string Key => "table-naming";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var tables = project.Tables
            .Where(table => !ReviewRuleHelpers.IsGeneratedTable(table.Name, options))
            .Where(table => table.Measures.Count == 0)
            .ToArray();
        var pattern = ReviewRuleHelpers.Compile(options.TableNamePattern, nameof(options.TableNamePattern));
        var findings = tables
            .Where(table => !pattern.IsMatch(table.Name))
            .Select(table => new PbipReviewFinding(
                "NAM-001",
                string.Empty,
                $"Table '{table.Name}' does not match the configured naming pattern.",
                table.FileName,
                $"Rename the table to match '{options.TableNamePattern}'."))
            .ToArray();
        return new ReviewRuleResult(tables.Length, findings);
    }
}

public sealed class ColumnNamingRule : IReviewRule
{
    public string Key => "column-naming";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var pattern = ReviewRuleHelpers.Compile(options.ColumnNamePattern, nameof(options.ColumnNamePattern));
        var columns = project.Tables
            .Where(table => !ReviewRuleHelpers.IsGeneratedTable(table.Name, options))
            .SelectMany(table => table.Columns.Select(column => (Table: table, Column: column)))
            .ToArray();
        var findings = columns
            .Where(item => !pattern.IsMatch(item.Column.Name))
            .Select(item => new PbipReviewFinding(
                "NAM-002",
                string.Empty,
                $"Column '{item.Column.Name}' in table '{item.Table.Name}' does not match the configured naming pattern.",
                item.Table.FileName,
                $"Rename the column to match '{options.ColumnNamePattern}', preserving any required source mapping."))
            .ToArray();
        return new ReviewRuleResult(columns.Length, findings);
    }
}

public sealed class PlaceholderColumnRule : IReviewRule
{
    public string Key => "placeholder-column";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var pattern = ReviewRuleHelpers.Compile(
            options.PlaceholderColumnPattern,
            nameof(options.PlaceholderColumnPattern));
        var columns = project.Tables
            .Where(table => !ReviewRuleHelpers.IsGeneratedTable(table.Name, options))
            .SelectMany(table => table.Columns.Select(column => (Table: table, Column: column)))
            .ToArray();
        var findings = columns
            .Where(item => pattern.IsMatch(item.Column.Name))
            .Select(item => new PbipReviewFinding(
                "NAM-003",
                string.Empty,
                $"Column '{item.Column.Name}' in table '{item.Table.Name}' looks like a default placeholder.",
                item.Table.FileName,
                "Rename or remove the placeholder column if it is not intentionally required."))
            .ToArray();
        return new ReviewRuleResult(columns.Length, findings);
    }
}

public sealed class MeasureTableNameRule : IReviewRule
{
    public string Key => "measure-table-name";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var tables = project.Tables
            .Where(table => table.Measures.Count > 0)
            .Where(table => options.MeasureTableNameCandidates.Contains(
                table.Name,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var findings = tables
            .Where(table => !string.Equals(
                table.Name,
                options.MeasureTableName,
                StringComparison.OrdinalIgnoreCase))
            .Select(table => new PbipReviewFinding(
                "NAM-004",
                string.Empty,
                $"Measure container table '{table.Name}' should be named '{options.MeasureTableName}'.",
                table.FileName,
                $"Rename the measure container to '{options.MeasureTableName}'."))
            .ToArray();
        return new ReviewRuleResult(tables.Length, findings);
    }
}

public sealed class MeasureNamingRule : IReviewRule
{
    public string Key => "measure-naming";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var pattern = ReviewRuleHelpers.Compile(options.MeasureNamePattern, nameof(options.MeasureNamePattern));
        var measures = project.Tables.SelectMany(table =>
            table.Measures.Select(measure => (Table: table, Measure: measure))).ToArray();
        var findings = measures
            .Where(item => !pattern.IsMatch(item.Measure.Name))
            .Select(item => new PbipReviewFinding(
                "NAM-005",
                string.Empty,
                $"Measure '{item.Measure.Name}' does not match the configured naming pattern.",
                item.Table.FileName,
                $"Rename the measure to match '{options.MeasureNamePattern}'."))
            .ToArray();
        return new ReviewRuleResult(measures.Length, findings);
    }
}

public sealed class RequiredMeasuresRule : IReviewRule
{
    public string Key => "required-measures";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var expected = options.RequiredMeasures
            .Where(static measure => !string.IsNullOrWhiteSpace(measure))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expected.Length == 0)
        {
            return new ReviewRuleResult(0, []);
        }

        var existing = project.Tables
            .SelectMany(static table => table.Measures)
            .Select(static measure => measure.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expected.Where(measure => !existing.Contains(measure)).ToArray();
        var findings = missing.Length == 0
            ? []
            : new[]
            {
                new PbipReviewFinding(
                    "MEAS-001",
                    string.Empty,
                    $"Required measures are missing: {string.Join(", ", missing)}.",
                    "definition/tables",
                    "Add the missing measures or update the configured required-measure list.")
            };
        return new ReviewRuleResult(expected.Length, findings);
    }
}

public sealed class RelationshipColumnsRule : IReviewRule
{
    public string Key => "relationship-columns";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var tables = project.Tables.ToDictionary(static table => table.Name, StringComparer.OrdinalIgnoreCase);
        var relationships = ReviewRuleHelpers.ModelRelationships(project, options);
        var findings = new List<PbipReviewFinding>();
        foreach (var relationship in relationships)
        {
            var fromExists = tables.TryGetValue(relationship.FromTable, out var fromTable) &&
                             fromTable.Columns.Any(column =>
                                 string.Equals(column.Name, relationship.FromColumn, StringComparison.OrdinalIgnoreCase));
            var toExists = tables.TryGetValue(relationship.ToTable, out var toTable) &&
                           toTable.Columns.Any(column =>
                               string.Equals(column.Name, relationship.ToColumn, StringComparison.OrdinalIgnoreCase));
            if (!fromExists || !toExists)
            {
                findings.Add(new PbipReviewFinding(
                    "REL-002",
                    string.Empty,
                    $"Relationship references an unknown column: {relationship.FromTable}[{relationship.FromColumn}] to {relationship.ToTable}[{relationship.ToColumn}].",
                    "definition/relationships.tmdl",
                    "Update the relationship to reference existing model tables and columns."));
            }
        }

        return new ReviewRuleResult(relationships.Length, findings);
    }
}

public sealed class StarSchemaRelationshipRule : IReviewRule
{
    public string Key => "star-schema-relationships";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var relationships = ReviewRuleHelpers.ModelRelationships(project, options);
        var findings = relationships
            .Where(relationship =>
            {
                var fromFact = relationship.FromTable.StartsWith(options.FactTablePrefix, StringComparison.OrdinalIgnoreCase);
                var toFact = relationship.ToTable.StartsWith(options.FactTablePrefix, StringComparison.OrdinalIgnoreCase);
                var fromDimension = relationship.FromTable.StartsWith(options.DimensionTablePrefix, StringComparison.OrdinalIgnoreCase);
                var toDimension = relationship.ToTable.StartsWith(options.DimensionTablePrefix, StringComparison.OrdinalIgnoreCase);
                return !((fromFact && toDimension) || (fromDimension && toFact));
            })
            .Select(relationship => new PbipReviewFinding(
                "REL-003",
                string.Empty,
                $"Relationship between '{relationship.FromTable}' and '{relationship.ToTable}' does not follow the configured Fact/Dimension naming convention.",
                "definition/relationships.tmdl",
                $"Review the relationship and configure prefixes '{options.FactTablePrefix}' and '{options.DimensionTablePrefix}' to match your standards."))
            .ToArray();
        return new ReviewRuleResult(relationships.Length, findings);
    }
}

public sealed class SingleDirectionRelationshipRule : IReviewRule
{
    public string Key => "single-direction-relationships";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var relationships = ReviewRuleHelpers.ModelRelationships(project, options);
        var findings = relationships
            .Where(static relationship => relationship.IsBidirectional)
            .Select(relationship => new PbipReviewFinding(
                "REL-004",
                string.Empty,
                $"Relationship between '{relationship.FromTable}' and '{relationship.ToTable}' uses bidirectional filtering.",
                "definition/relationships.tmdl",
                "Use single-direction filtering by default; retain bidirectional filtering only with a documented requirement."))
            .ToArray();
        return new ReviewRuleResult(relationships.Length, findings);
    }
}

public sealed class ExplicitMeasuresRule : IReviewRule
{
    public string Key => "explicit-measures";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        var measureCount = project.Tables.Sum(static table => table.Measures.Count);
        var findings = measureCount == 0
            ? new[]
            {
                new PbipReviewFinding(
                    "DAX-001",
                    string.Empty,
                    "The semantic model contains no explicit DAX measures.",
                    "definition/tables",
                    "Create named measures for business calculations rather than relying only on implicit visual aggregations.")
            }
            : [];
        return new ReviewRuleResult(1, findings);
    }
}

public sealed class DaxDivisionRule : IReviewRule
{
    private static readonly Regex CommentsAndStrings = new(
        @"//.*?$|/\*.*?\*/|""(?:[^""]|"""")*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);

    private static readonly Regex DivideCall = new(
        @"\bDIVIDE\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DivisionOperator = new(
        @"(?<!/)/(?!/)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Key => "dax-division";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        if (!options.RequireDivideForDivision)
        {
            return new ReviewRuleResult(0, []);
        }

        var measures = project.Tables.SelectMany(static table => table.Measures).ToArray();
        var findings = measures
            .Where(measure =>
            {
                var expression = CommentsAndStrings.Replace(measure.Expression, string.Empty);
                return DivisionOperator.IsMatch(expression) && !DivideCall.IsMatch(expression);
            })
            .Select(measure => new PbipReviewFinding(
                "DAX-002",
                string.Empty,
                $"Measure '{measure.Name}' uses the division operator instead of DIVIDE.",
                "definition/tables",
                "Consider DIVIDE(numerator, denominator, alternateResult) to handle zero or blank denominators safely."))
            .ToArray();
        return new ReviewRuleResult(measures.Length, findings);
    }
}

public sealed class DaxFormatRule : IReviewRule
{
    private static readonly Regex CommentsAndStrings = new(
        @"//.*?$|/\*.*?\*/|""(?:[^""]|"""")*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);

    private static readonly Regex FormatCall = new(
        @"\bFORMAT\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string Key => "dax-format";

    public ReviewRuleResult Evaluate(PbipProjectModel project, ReviewOptions options)
    {
        if (!options.WarnOnFormatInDax)
        {
            return new ReviewRuleResult(0, []);
        }

        var measures = project.Tables.SelectMany(static table => table.Measures).ToArray();
        var findings = measures
            .Where(measure => FormatCall.IsMatch(CommentsAndStrings.Replace(measure.Expression, string.Empty)))
            .Select(measure => new PbipReviewFinding(
                "DAX-003",
                string.Empty,
                $"Measure '{measure.Name}' calls FORMAT, which returns text.",
                "definition/tables",
                "Prefer the measure's model format string for numeric/date formatting so the result remains usable in numeric calculations."))
            .ToArray();
        return new ReviewRuleResult(measures.Length, findings);
    }
}

internal static class ReviewRuleHelpers
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static Regex Compile(string pattern, string optionName)
    {
        try
        {
            return new Regex(
                pattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException($"Review option '{optionName}' is not a valid regular expression.", exception);
        }
    }

    public static bool IsGeneratedTable(string tableName, ReviewOptions options) =>
        options.GeneratedTablePrefixes.Any(prefix =>
            tableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static ModelRelationship[] ModelRelationships(PbipProjectModel project, ReviewOptions options) =>
        project.Relationships
            .Where(relationship =>
                !IsGeneratedTable(relationship.FromTable, options) &&
                !IsGeneratedTable(relationship.ToTable, options))
            .ToArray();
}
