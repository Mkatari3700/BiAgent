using System.Text.Json;
using System.Text.RegularExpressions;

namespace BISentinel.Api.Reviews;

public sealed class PbipProjectReader : IPbipProjectReader
{
    private static readonly Regex TableDeclaration = new(
        @"^\s*table\s+(?:'(?<quoted>[^']+)'|(?<plain>[^\s]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ColumnDeclaration = new(
        @"^\s*column\s+(?:'(?<quoted>[^']+)'|(?<plain>[^=\s]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MeasureDeclaration = new(
        @"^\s*measure\s+(?:'(?<quoted>[^']+)'|(?<plain>[^=\s]+))\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RelationshipDeclaration = new(
        @"(?ms)^relationship\s+\S+\s*(?<body>.*?)(?=^relationship\s+|\z)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RelationshipEndpoint = new(
        @"^(?:'(?<quotedTable>[^']+)'|(?<plainTable>[^.]+))\.(?:'(?<quotedColumn>[^']+)'|(?<plainColumn>.+))$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MeasureMetadata = new(
        @"^\s*(?:lineageTag|formatString|formatStringDefinition|displayFolder|description|annotation|isHidden|dataType)\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PbipProjectModel Read(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new InvalidDataException("The selected Power BI project folder does not exist.");
        }

        var fullPath = Path.GetFullPath(projectPath);
        var selectedProjectFile = File.Exists(fullPath) &&
            Path.GetExtension(fullPath).Equals(".pbip", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        if (selectedProjectFile is null && !Directory.Exists(fullPath))
        {
            throw new InvalidDataException("The selected Power BI project folder does not exist.");
        }

        var projectRoot = selectedProjectFile is null
            ? fullPath
            : Path.GetDirectoryName(fullPath)!;
        var projectFiles = selectedProjectFile is null
            ? Directory.GetFiles(projectRoot, "*.pbip", SearchOption.TopDirectoryOnly)
            : [selectedProjectFile];
        if (projectFiles.Length > 1)
        {
            throw new InvalidDataException("The selected folder contains more than one .pbip project file.");
        }

        if (projectFiles.Length == 0)
        {
            if (File.Exists(Path.Combine(projectRoot, ".pbixproj.json")))
            {
                return ReadExtractedProject(projectRoot);
            }

            if (File.Exists(Path.Combine(projectRoot, "definition.pbir")))
            {
                return ReadPbirProject(projectRoot);
            }

            throw new InvalidDataException(
                "The selected folder is not a supported PBIP project, standalone PBIR report, or extracted Power BI model.");
        }

        using var projectDescriptor = ParseJson(projectFiles[0], "The PBIP project descriptor is not valid JSON.");
        var reportRelativePath = ReadProjectReportPath(projectDescriptor.RootElement);
        var reportDirectory = ResolveDirectory(projectRoot, projectRoot, reportRelativePath);
        var reportDescriptorPath = ResolveFile(projectRoot, reportDirectory, "definition.pbir");
        using var reportDescriptor = ParseJson(
            reportDescriptorPath,
            "The report definition descriptor is missing or is not valid JSON.");

        var semanticModelRelativePath = ReadSemanticModelPath(reportDescriptor.RootElement);
        var semanticModelDirectory = ResolveDirectory(reportDirectory, projectRoot, semanticModelRelativePath);
        var modelDefinitionDirectory = ResolveDirectory(semanticModelDirectory, projectRoot, "definition");
        var tablesDirectory = ResolveDirectory(modelDefinitionDirectory, projectRoot, "tables");

        var requiredFiles = new[]
        {
            Path.Combine(reportDirectory, "definition", "report.json"),
            Path.Combine(reportDirectory, "definition", "pages", "pages.json"),
            Path.Combine(semanticModelDirectory, "definition.pbism"),
            Path.Combine(modelDefinitionDirectory, "model.tmdl"),
            Path.Combine(modelDefinitionDirectory, "relationships.tmdl")
        };

        var tables = PbipProjectMetadata.DeduplicateTables(
            Directory.GetFiles(tablesDirectory, "*.tmdl", SearchOption.TopDirectoryOnly)
                .Select(ParseTable));
        if (tables.Count == 0)
        {
            throw new InvalidDataException("The semantic model contains no table definition files.");
        }

        var relationshipsFile = Path.Combine(modelDefinitionDirectory, "relationships.tmdl");
        var relationships = File.Exists(relationshipsFile)
            ? ParseRelationships(relationshipsFile)
            : [];

        return new PbipProjectModel(
            Path.GetFileNameWithoutExtension(projectFiles[0]),
            requiredFiles,
            tables,
            PbipProjectMetadata.DeduplicateRelationships(relationships),
            ReportReferences: ReadReportReferences(Path.Combine(reportDirectory, "definition")));
    }

    private static PbipProjectModel ReadPbirProject(string projectRoot)
    {
        var definitionDirectory = Path.Combine(projectRoot, "definition");
        var requiredFiles = new[]
        {
            Path.Combine(projectRoot, "definition.pbir"),
            Path.Combine(definitionDirectory, "report.json"),
            Path.Combine(definitionDirectory, "pages", "pages.json")
        };

        foreach (var requiredFile in requiredFiles)
        {
            using var _ = ParseJson(
                requiredFile,
                $"The standalone PBIR report is missing valid JSON file '{Path.GetRelativePath(projectRoot, requiredFile)}'.");
        }

        return new PbipProjectModel(
            Path.GetFileName(projectRoot),
            requiredFiles,
            [],
            [],
            HasSemanticModel: false);
    }

    private static PbipProjectModel ReadExtractedProject(string projectRoot)
    {
        var modelFiles = Directory.GetFiles(projectRoot, "*.tmdl", SearchOption.AllDirectories);
        var tables = PbipProjectMetadata.DeduplicateTables(modelFiles
            .Where(path => File.ReadLines(path).Any(line => TableDeclaration.IsMatch(line)))
            .Select(path =>
            {
                var table = ParseTable(path);
                return table with { FileName = Path.GetRelativePath(projectRoot, path) };
            }));
        if (tables.Count == 0)
        {
            throw new InvalidDataException(
                "The extracted Power BI model does not contain TMDL table definitions. Configure pbi-tools to serialize models as TMDL.");
        }

        var relationships = PbipProjectMetadata.DeduplicateRelationships(
            modelFiles
                .Where(path => Path.GetFileName(path).Equals("relationships.tmdl", StringComparison.OrdinalIgnoreCase))
                .SelectMany(ParseRelationships));

        return new PbipProjectModel(
            Path.GetFileName(projectRoot),
            [],
            tables,
            relationships);
    }

    private static JsonDocument ParseJson(string path, string errorMessage)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException(errorMessage);
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(errorMessage, exception);
        }
    }

    private static string ReadProjectReportPath(JsonElement descriptor)
    {
        if (descriptor.TryGetProperty("artifacts", out var artifacts) &&
            artifacts.ValueKind == JsonValueKind.Array)
        {
            foreach (var artifact in artifacts.EnumerateArray())
            {
                if (artifact.TryGetProperty("report", out var report) &&
                    report.TryGetProperty("path", out var path) &&
                    path.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(path.GetString()))
                {
                    return path.GetString()!;
                }
            }
        }

        throw new InvalidDataException("The PBIP descriptor must declare a report artifact path.");
    }

    private static string ReadSemanticModelPath(JsonElement descriptor)
    {
        if (descriptor.TryGetProperty("datasetReference", out var datasetReference) &&
            datasetReference.TryGetProperty("byPath", out var byPath) &&
            byPath.TryGetProperty("path", out var path) &&
            path.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(path.GetString()))
        {
            return path.GetString()!;
        }

        throw new InvalidDataException(
            "The PBIP report must reference a semantic model by project path for this review.");
    }

    private static IReadOnlyList<string>? ReadReportReferences(string reportDefinitionDirectory) =>
        Directory.Exists(reportDefinitionDirectory)
            ? Directory.GetFiles(reportDefinitionDirectory, "*.json", SearchOption.AllDirectories)
                .Select(File.ReadAllText)
                .ToArray()
            : null;

    private static string ResolveDirectory(string baseDirectory, string projectRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("PBIP project references must be relative paths within the project folder.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        var rootPrefix = projectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? projectRoot
            : projectRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PBIP project references must remain within the configured project folder.");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new InvalidDataException("A referenced PBIP project folder does not exist.");
        }

        return fullPath;
    }

    private static string ResolveFile(string projectRoot, string directory, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
        var rootPrefix = projectRoot.EndsWith(Path.DirectorySeparatorChar)
            ? projectRoot
            : projectRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PBIP project files must remain within the configured project folder.");
        }

        return fullPath;
    }

    private static ModelTable ParseTable(string path)
    {
        var lines = File.ReadAllLines(path);
        var tableName = lines
            .Select(line => TableDeclaration.Match(line))
            .Where(static match => match.Success)
            .Select(GetName)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new InvalidDataException($"A TMDL table definition is invalid: {Path.GetFileName(path)}.");
        }

        var columns = lines
            .Select(line => ColumnDeclaration.Match(line))
            .Where(static match => match.Success)
            .Select(GetName)
            .Select(static name => new ModelColumn(name))
            .ToArray();

        return new ModelTable(tableName, Path.GetFileName(path), columns, ParseMeasures(lines));
    }

    private static IReadOnlyList<ModelMeasure> ParseMeasures(IReadOnlyList<string> lines)
    {
        var measures = new List<ModelMeasure>();
        string? currentName = null;
        var expression = new List<string>();

        void StoreCurrent()
        {
            if (currentName is not null)
            {
                measures.Add(new ModelMeasure(currentName, string.Join(Environment.NewLine, expression)));
            }

            currentName = null;
            expression.Clear();
        }

        foreach (var line in lines)
        {
            var declaration = MeasureDeclaration.Match(line);
            if (declaration.Success)
            {
                StoreCurrent();
                currentName = GetName(declaration);
                continue;
            }

            if (currentName is null)
            {
                continue;
            }

            if (MeasureMetadata.IsMatch(line))
            {
                StoreCurrent();
                continue;
            }

            expression.Add(line);
        }

        StoreCurrent();
        return measures;
    }

    private static IReadOnlyList<ModelRelationship> ParseRelationships(string path)
    {
        var content = File.ReadAllText(path);
        return RelationshipDeclaration.Matches(content)
            .Select(match =>
            {
                var body = match.Groups["body"].Value;
                var from = ReadRelationshipProperty(body, "fromColumn");
                var to = ReadRelationshipProperty(body, "toColumn");
                return new ModelRelationship(
                    from.Table,
                    from.Column,
                    to.Table,
                    to.Column,
                    body.Contains("crossFilteringBehavior: bothDirections", StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();
    }

    private static (string Table, string Column) ReadRelationshipProperty(string body, string property)
    {
        var match = Regex.Match(
            body,
            $@"(?m)^\s*{Regex.Escape(property)}:\s*(.+?)\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidDataException($"A relationship is missing its {property} reference.");
        }

        var endpoint = RelationshipEndpoint.Match(match.Groups[1].Value);
        if (!endpoint.Success)
        {
            throw new InvalidDataException("A relationship contains an unsupported column reference.");
        }

        var table = endpoint.Groups["quotedTable"].Success
            ? endpoint.Groups["quotedTable"].Value
            : endpoint.Groups["plainTable"].Value.Trim();
        var column = endpoint.Groups["quotedColumn"].Success
            ? endpoint.Groups["quotedColumn"].Value
            : endpoint.Groups["plainColumn"].Value.Trim();
        return (table, column);
    }

    private static string GetName(Match match) =>
        match.Groups["quoted"].Success
            ? match.Groups["quoted"].Value
            : match.Groups["plain"].Value;
}

internal static class PbipProjectMetadata
{
    public static IReadOnlyList<ModelTable> DeduplicateTables(IEnumerable<ModelTable> tables) =>
        tables
            .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var candidates = group
                    .OrderBy(table => table.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var columns = candidates
                    .SelectMany(table => table.Columns)
                    .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(columnsForName => columnsForName.First())
                    .OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var measures = candidates
                    .SelectMany(table => table.Measures)
                    .GroupBy(measure => measure.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(measuresForName => measuresForName
                        .OrderByDescending(measure => measure.Expression.Length)
                        .First())
                    .OrderBy(measure => measure.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return candidates[0] with { Columns = columns, Measures = measures };
            })
            .OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<ModelRelationship> DeduplicateRelationships(
        IEnumerable<ModelRelationship> relationships) =>
        relationships
            .GroupBy(relationship =>
            {
                var from = $"{relationship.FromTable}\0{relationship.FromColumn}";
                var to = $"{relationship.ToTable}\0{relationship.ToColumn}";
                var (first, second) = StringComparer.OrdinalIgnoreCase.Compare(from, to) <= 0
                    ? (from, to)
                    : (to, from);
                return $"{first}\u001e{second}";
            }, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var relationship = group
                    .OrderBy(item => item.FromTable, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.FromColumn, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ToTable, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ToColumn, StringComparer.OrdinalIgnoreCase)
                    .First();
                return relationship with { IsBidirectional = group.Any(item => item.IsBidirectional) };
            })
            .OrderBy(item => item.FromTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FromColumn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ToTable, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ToColumn, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
