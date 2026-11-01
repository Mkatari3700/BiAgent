using System.Text.Json;
using System.Text.RegularExpressions;

namespace BISentinel.Api.Reviews;

public sealed class PbipProjectReader : IPbipProjectReader
{
    private static readonly Regex TableDeclaration = new(
        @"^\s*table\s+(?:'(?<quoted>[^']+)'|(?<plain>[^\s]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ColumnDeclaration = new(
        @"^\s*column\s+(?:'(?<quoted>[^']+)'|(?<plain>[^=\s]+))(?<calculated>\s*=\s*(?<inline>.*))?",
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

    private static readonly Regex PartitionDeclaration = new(
        @"^\s*partition\s+(?:'(?<quoted>[^']+)'|(?<plain>[^\s=]+))\s*=\s*(?<sourceType>\w+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HierarchyDeclaration = new(
        @"^\s*hierarchy\s+(?:'(?<quoted>[^']+)'|(?<plain>[^\s=]+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HierarchyLevelDeclaration = new(
        @"^\s*level\s+(?:'(?<quoted>[^']+)'|(?<plain>[^\s=]+))",
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
        if (File.Exists(fullPath) &&
            !Path.GetExtension(fullPath).Equals(".pbip", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".bim" => ReadModelBim(fullPath),
                ".tmdl" => ReadTmdlFile(fullPath),
                ".dax" => ReadDaxFile(fullPath),
                _ => throw new InvalidDataException(
                    "Upload a PBIP/PBIR project, PBIT template, Model.bim, TMDL, or DAX file.")
            };
        }

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
            var bimFiles = Directory.GetFiles(projectRoot, "*.bim", SearchOption.AllDirectories);
            if (bimFiles.Length == 1)
            {
                return ReadModelBim(bimFiles[0], projectRoot);
            }
            if (bimFiles.Length > 1)
            {
                throw new InvalidDataException("The uploaded project contains more than one Model.bim file.");
            }

            if (File.Exists(Path.Combine(projectRoot, ".pbixproj.json")))
            {
                return ReadExtractedProject(projectRoot);
            }

            if (File.Exists(Path.Combine(projectRoot, "definition.pbir")))
            {
                return ReadPbirProject(projectRoot);
            }

            if (Directory.GetFiles(projectRoot, "*.tmdl", SearchOption.AllDirectories).Length > 0)
            {
                return ReadExtractedProject(projectRoot);
            }

            var daxFiles = Directory.GetFiles(projectRoot, "*.dax", SearchOption.AllDirectories);
            if (daxFiles.Length > 0)
            {
                return ReadDaxFiles(projectRoot, daxFiles);
            }

            throw new InvalidDataException(
                "The selected folder does not contain a supported PBIP/PBIR project, Model.bim, TMDL model, or DAX file.");
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

        var reportFiles = ReadReportFiles(Path.Combine(reportDirectory, "definition"), projectRoot);
        return new PbipProjectModel(
            Path.GetFileNameWithoutExtension(projectFiles[0]),
            requiredFiles,
            tables,
            PbipProjectMetadata.DeduplicateRelationships(relationships),
            ReportReferences: reportFiles.Select(file => file.Content).ToArray())
        {
            ReportFiles = reportFiles
        };
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

        var reportFiles = ReadReportFiles(definitionDirectory, projectRoot);
        return new PbipProjectModel(
            Path.GetFileName(projectRoot),
            requiredFiles,
            [],
            [],
            HasSemanticModel: false,
            ReportReferences: reportFiles.Select(file => file.Content).ToArray())
        {
            ReportFiles = reportFiles
        };
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

        var reportFiles = ReadReportFilesFromProject(projectRoot);
        return new PbipProjectModel(
            Path.GetFileName(projectRoot),
            [],
            tables,
            relationships,
            ReportReferences: reportFiles.Count == 0
                ? null
                : reportFiles.Select(file => file.Content).ToArray())
        {
            ReportFiles = reportFiles
        };
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

        var partitions = ParsePartitions(lines);
        return new ModelTable(
            tableName,
            Path.GetFileName(path),
            ParseColumns(lines),
            ParseMeasures(lines))
        {
            IsCalculated = partitions.Any(partition =>
                string.Equals(partition.SourceType, "calculated", StringComparison.OrdinalIgnoreCase)),
            Partitions = partitions,
            Hierarchies = ParseHierarchies(lines)
        };
    }

    private static IReadOnlyList<ModelColumn> ParseColumns(IReadOnlyList<string> lines)
    {
        var columns = new List<ModelColumn>();
        for (var index = 0; index < lines.Count; index++)
        {
            var declaration = ColumnDeclaration.Match(lines[index]);
            if (!declaration.Success)
            {
                continue;
            }

            var name = GetName(declaration);
            var isCalculated = declaration.Groups["calculated"].Success;
            string? dataType = null;
            var isHidden = false;
            var expression = new List<string>();
            if (!string.IsNullOrWhiteSpace(declaration.Groups["inline"].Value))
            {
                expression.Add(declaration.Groups["inline"].Value.Trim());
            }
            for (var next = index + 1; next < lines.Count; next++)
            {
                var line = lines[next];
                if (TableDeclaration.IsMatch(line) ||
                    ColumnDeclaration.IsMatch(line) ||
                    MeasureDeclaration.IsMatch(line) ||
                    PartitionDeclaration.IsMatch(line) ||
                    HierarchyDeclaration.IsMatch(line))
                {
                    break;
                }

                if (TryReadProperty(line, "dataType", out var typeValue))
                {
                    dataType = typeValue;
                }
                else if (TryReadProperty(line, "isHidden", out var hiddenValue))
                {
                    isHidden = bool.TryParse(hiddenValue, out var hidden) && hidden;
                }
                else if (Regex.IsMatch(line, @"^\s*[A-Za-z][A-Za-z0-9.]*:\s*"))
                {
                    continue;
                }
                else if (isCalculated && !string.IsNullOrWhiteSpace(line))
                {
                    expression.Add(line);
                }
            }

            columns.Add(new ModelColumn(
                name,
                dataType,
                isCalculated,
                isCalculated ? string.Join(Environment.NewLine, expression).Trim() : null,
                isHidden));
        }

        return columns;
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
                var fromCardinality = ReadOptionalProperty(body, "fromCardinality");
                var toCardinality = ReadOptionalProperty(body, "toCardinality");
                var isActive = bool.TryParse(ReadOptionalProperty(body, "isActive"), out var active)
                    ? active
                    : (bool?)null;
                var crossFilteringBehavior = ReadOptionalProperty(body, "crossFilteringBehavior");
                return new ModelRelationship(
                    from.Table,
                    from.Column,
                    to.Table,
                    to.Column,
                    string.Equals(
                        crossFilteringBehavior,
                        "bothDirections",
                        StringComparison.OrdinalIgnoreCase),
                    fromCardinality,
                    toCardinality,
                    isActive,
                    crossFilteringBehavior);
            })
            .ToArray();
    }

    private static IReadOnlyList<ModelPartition> ParsePartitions(IReadOnlyList<string> lines)
    {
        var partitions = new List<ModelPartition>();
        for (var index = 0; index < lines.Count; index++)
        {
            var declaration = PartitionDeclaration.Match(lines[index]);
            if (!declaration.Success)
            {
                continue;
            }

            var mode = (string?)null;
            for (var next = index + 1; next < lines.Count; next++)
            {
                if (TableDeclaration.IsMatch(lines[next]) ||
                    ColumnDeclaration.IsMatch(lines[next]) ||
                    MeasureDeclaration.IsMatch(lines[next]) ||
                    PartitionDeclaration.IsMatch(lines[next]) ||
                    HierarchyDeclaration.IsMatch(lines[next]))
                {
                    break;
                }

                if (TryReadProperty(lines[next], "mode", out var modeValue))
                {
                    mode = modeValue;
                }
            }

            partitions.Add(new ModelPartition(
                GetName(declaration),
                mode,
                declaration.Groups["sourceType"].Value));
        }

        return partitions;
    }

    private static IReadOnlyList<ModelHierarchy> ParseHierarchies(IReadOnlyList<string> lines)
    {
        var hierarchies = new List<ModelHierarchy>();
        for (var index = 0; index < lines.Count; index++)
        {
            var declaration = HierarchyDeclaration.Match(lines[index]);
            if (!declaration.Success)
            {
                continue;
            }

            var levels = new List<string>();
            for (var next = index + 1; next < lines.Count; next++)
            {
                if (TableDeclaration.IsMatch(lines[next]) ||
                    ColumnDeclaration.IsMatch(lines[next]) ||
                    MeasureDeclaration.IsMatch(lines[next]) ||
                    PartitionDeclaration.IsMatch(lines[next]) ||
                    HierarchyDeclaration.IsMatch(lines[next]))
                {
                    break;
                }

                var level = HierarchyLevelDeclaration.Match(lines[next]);
                if (level.Success)
                {
                    levels.Add(GetName(level));
                }
            }

            hierarchies.Add(new ModelHierarchy(GetName(declaration), levels));
        }

        return hierarchies;
    }

    private static PbipProjectModel ReadTmdlFile(string path)
    {
        var relationships = Path.GetFileName(path).Equals("relationships.tmdl", StringComparison.OrdinalIgnoreCase)
            ? ParseRelationships(path)
            : [];
        ModelTable[] tables = File.ReadLines(path).Any(line => TableDeclaration.IsMatch(line))
            ? [ParseTable(path)]
            : [];
        if (tables.Length == 0 && relationships.Count == 0)
        {
            throw new InvalidDataException("The TMDL file does not contain a table or relationship definition.");
        }

        return new PbipProjectModel(
            Path.GetFileNameWithoutExtension(path),
            [],
            tables,
            relationships,
            HasSemanticModel: true);
    }

    private static PbipProjectModel ReadDaxFile(string path) =>
        ReadDaxFiles(Path.GetDirectoryName(path)!, [path]);

    private static PbipProjectModel ReadDaxFiles(string root, IEnumerable<string> daxFiles)
    {
        var measures = daxFiles
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ModelMeasure(
                Path.GetFileNameWithoutExtension(path),
                File.ReadAllText(path)))
            .ToArray();
        return new PbipProjectModel(
            Path.GetFileName(root),
            [],
            [new ModelTable("DAX", "DAX", [], measures)],
            [],
            HasSemanticModel: false);
    }

    private static PbipProjectModel ReadModelBim(string path, string? projectRoot = null)
    {
        using var document = ParseJson(path, "The Model.bim file is not valid JSON.");
        var model = TryGetProperty(document.RootElement, "model", out var modelElement)
            ? modelElement
            : document.RootElement;
        var tables = TryGetProperty(model, "tables", out var tablesElement) &&
            tablesElement.ValueKind == JsonValueKind.Array
                ? tablesElement.EnumerateArray().Select(ParseBimTable).ToArray()
                : [];
        var relationships = TryGetProperty(model, "relationships", out var relationshipsElement) &&
            relationshipsElement.ValueKind == JsonValueKind.Array
                ? relationshipsElement.EnumerateArray().Select(ParseBimRelationship).ToArray()
                : [];
        if (tables.Length == 0)
        {
            throw new InvalidDataException("The Model.bim file does not contain semantic-model tables.");
        }

        var reportDefinition = projectRoot is null ? null : FindReportDefinitionDirectory(projectRoot);
        var reportFiles = reportDefinition is null
            ? []
            : ReadReportFiles(reportDefinition, projectRoot!);
        return new PbipProjectModel(
            ReadString(model, "name") ?? Path.GetFileNameWithoutExtension(path),
            [],
            PbipProjectMetadata.DeduplicateTables(tables),
            PbipProjectMetadata.DeduplicateRelationships(relationships),
            ReportReferences: reportFiles.Count == 0
                ? null
                : reportFiles.Select(file => file.Content).ToArray())
        {
            ReportFiles = reportFiles
        };
    }

    private static ModelTable ParseBimTable(JsonElement table)
    {
        var name = ReadString(table, "name")
            ?? throw new InvalidDataException("A Model.bim table is missing its name.");
        var columns = ReadArray(table, "columns")
            .Select(column => new ModelColumn(
                ReadString(column, "name") ?? "Unknown",
                ReadString(column, "dataType"),
                string.Equals(ReadString(column, "type"), "calculated", StringComparison.OrdinalIgnoreCase) ||
                    ReadString(column, "expression") is not null,
                ReadString(column, "expression"),
                ReadBoolean(column, "isHidden") ?? false))
            .ToArray();
        var measures = ReadArray(table, "measures")
            .Select(measure => new ModelMeasure(
                ReadString(measure, "name") ?? "Unknown",
                ReadString(measure, "expression") ?? string.Empty))
            .ToArray();
        var partitions = ReadArray(table, "partitions")
            .Select(partition =>
            {
                var sourceType = TryGetProperty(partition, "source", out var source)
                    ? ReadString(source, "type")
                    : null;
                return new ModelPartition(
                    ReadString(partition, "name") ?? name,
                    ReadString(partition, "mode"),
                    sourceType);
            })
            .ToArray();
        var hierarchies = ReadArray(table, "hierarchies")
            .Select(hierarchy => new ModelHierarchy(
                ReadString(hierarchy, "name") ?? "Unknown",
                ReadArray(hierarchy, "levels")
                    .Select(level => ReadString(level, "name") ?? "Unknown")
                    .ToArray()))
            .ToArray();
        return new ModelTable(name, name, columns, measures)
        {
            IsCalculated = partitions.Any(partition =>
                string.Equals(partition.SourceType, "calculated", StringComparison.OrdinalIgnoreCase)),
            Partitions = partitions,
            Hierarchies = hierarchies
        };
    }

    private static ModelRelationship ParseBimRelationship(JsonElement relationship)
    {
        var fromTable = ReadString(relationship, "fromTable")
            ?? throw new InvalidDataException("A Model.bim relationship is missing its fromTable.");
        var fromColumn = ReadString(relationship, "fromColumn")
            ?? throw new InvalidDataException("A Model.bim relationship is missing its fromColumn.");
        var toTable = ReadString(relationship, "toTable")
            ?? throw new InvalidDataException("A Model.bim relationship is missing its toTable.");
        var toColumn = ReadString(relationship, "toColumn")
            ?? throw new InvalidDataException("A Model.bim relationship is missing its toColumn.");
        var behavior = ReadString(relationship, "crossFilteringBehavior");
        return new ModelRelationship(
            fromTable,
            fromColumn,
            toTable,
            toColumn,
            string.Equals(behavior, "bothDirections", StringComparison.OrdinalIgnoreCase),
            ReadString(relationship, "fromCardinality"),
            ReadString(relationship, "toCardinality"),
            ReadBoolean(relationship, "isActive"),
            behavior);
    }

    private static IReadOnlyList<ModelReportFile> ReadReportFiles(string reportDirectory, string projectRoot) =>
        Directory.Exists(reportDirectory)
            ? Directory.GetFiles(reportDirectory, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new ModelReportFile(
                    Path.GetRelativePath(projectRoot, path),
                    File.ReadAllText(path)))
                .ToArray()
            : [];

    private static IReadOnlyList<ModelReportFile> ReadReportFilesFromProject(string projectRoot)
    {
        var reportDirectory = FindReportDefinitionDirectory(projectRoot);
        return reportDirectory is null ? [] : ReadReportFiles(reportDirectory, projectRoot);
    }

    private static string? FindReportDefinitionDirectory(string projectRoot)
    {
        var reportDescriptors = Directory.GetFiles(projectRoot, "definition.pbir", SearchOption.AllDirectories)
            .Where(path =>
            {
                var reportRoot = Path.GetDirectoryName(path)!;
                return File.Exists(Path.Combine(reportRoot, "definition", "report.json")) &&
                    File.Exists(Path.Combine(reportRoot, "definition", "pages", "pages.json"));
            })
            .ToArray();
        if (reportDescriptors.Length > 1)
        {
            throw new InvalidDataException("The uploaded artifact contains more than one report definition.");
        }

        return reportDescriptors.Length == 0
            ? null
            : Path.Combine(Path.GetDirectoryName(reportDescriptors[0])!, "definition");
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

    private static string? ReadOptionalProperty(string body, string property)
    {
        var match = Regex.Match(
            body,
            $@"(?m)^\s*{Regex.Escape(property)}:\s*(.+?)\s*$",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static bool TryReadProperty(string line, string property, out string value)
    {
        var match = Regex.Match(
            line,
            $@"^\s*{Regex.Escape(property)}:\s*(.*?)\s*$",
            RegexOptions.CultureInvariant);
        value = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        return match.Success;
    }

    private static bool TryGetProperty(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];

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
                var partitions = candidates
                    .SelectMany(table => table.Partitions)
                    .GroupBy(partition => partition.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();
                var hierarchies = candidates
                    .SelectMany(table => table.Hierarchies)
                    .GroupBy(hierarchy => hierarchy.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray();

                return candidates[0] with
                {
                    Columns = columns,
                    Measures = measures,
                    IsCalculated = candidates.Any(table => table.IsCalculated),
                    Partitions = partitions,
                    Hierarchies = hierarchies
                };
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
