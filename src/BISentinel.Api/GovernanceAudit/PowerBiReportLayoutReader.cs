using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BISentinel.Api.GovernanceAudit;

public sealed record PowerBiReportFieldReference(
    string? TableName,
    string FieldName,
    string? Kind,
    string? QueryReference);

public static class PowerBiReportLayoutReader
{
    private const int MaximumLayoutBytes = 25 * 1024 * 1024;

    public static IReadOnlyList<PowerBiReportFieldReference> ReadReferences(Stream exportedReport)
    {
        using var archive = new ZipArchive(exportedReport, ZipArchiveMode.Read, leaveOpen: true);
        var layout = archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName, "Report/Layout", StringComparison.OrdinalIgnoreCase));
        if (layout is not null)
        {
            var references = new HashSet<PowerBiReportFieldReference>();
            using var document = JsonDocument.Parse(DecodeLayout(ReadEntry(layout)));
            CollectReferences(document.RootElement, null, references, false);
            return references.ToArray();
        }

        var definitionFiles = archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith("Report/definition/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (definitionFiles.Length == 0)
        {
            throw new InvalidDataException(
                "The exported report contains neither the classic Report/Layout file nor supported PBIR definition JSON.");
        }

        var definitionBytes = 0L;
        var definitionReferences = new HashSet<PowerBiReportFieldReference>();
        foreach (var definitionFile in definitionFiles)
        {
            using var document = JsonDocument.Parse(ReadEntry(definitionFile, ref definitionBytes));
            CollectReferences(document.RootElement, null, definitionReferences, false);
        }

        return definitionReferences.ToArray();
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        long totalBytes = 0;
        return ReadEntry(entry, ref totalBytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, ref long totalBytes)
    {
        if (entry.Length <= 0 || entry.Length > MaximumLayoutBytes - totalBytes)
        {
            throw new InvalidDataException(
                "The exported report layout is empty or exceeds the 25 MB analysis limit.");
        }

        using var entryStream = entry.Open();
        using var content = new MemoryStream((int)entry.Length);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (content.Length > MaximumLayoutBytes - totalBytes - read)
            {
                throw new InvalidDataException("The exported report layout exceeds the 25 MB analysis limit.");
            }

            content.Write(buffer, 0, read);
        }

        totalBytes += content.Length;
        return content.ToArray();
    }

    private static string DecodeLayout(byte[] bytes)
    {
        var encoding = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode
            : bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
                ? Encoding.BigEndianUnicode
                : bytes.Length >= 2 && bytes[1] == 0
                    ? Encoding.Unicode
                    : Encoding.UTF8;
        var json = encoding.GetString(bytes);
        return json.TrimStart('\uFEFF', '\0', ' ', '\r', '\n', '\t');
    }

    private static void CollectReferences(
        JsonElement element,
        string? kind,
        HashSet<PowerBiReportFieldReference> references,
        bool isReferenceText)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var property = element.TryGetProperty("Property", out var propertyElement) &&
                propertyElement.ValueKind == JsonValueKind.String
                    ? propertyElement.GetString()
                    : null;
            if (!string.IsNullOrWhiteSpace(property))
            {
                var entity = FindEntity(element);
                references.Add(new PowerBiReportFieldReference(entity, property, kind, null));
            }

            foreach (var child in element.EnumerateObject())
            {
                var childKind = child.Name.Equals("Measure", StringComparison.OrdinalIgnoreCase)
                    ? "Measure"
                    : child.Name.Equals("Column", StringComparison.OrdinalIgnoreCase)
                        ? "Column"
                        : kind;
                var isReferenceProperty =
                    child.Name.Equals("queryRef", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.Equals("nativeQueryRef", StringComparison.OrdinalIgnoreCase);
                CollectReferences(child.Value, childKind, references, isReferenceProperty);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectReferences(child, kind, references, isReferenceText);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 100_000)
            {
                if (value.StartsWith('{') || value.StartsWith('['))
                {
                    try
                    {
                        using var nested = JsonDocument.Parse(value);
                        CollectReferences(nested.RootElement, kind, references, false);
                    }
                    catch (JsonException)
                    {
                        if (isReferenceText)
                        {
                            AddQueryReference(value, kind, references);
                        }
                    }
                }
                else if (isReferenceText)
                {
                    AddQueryReference(value, kind, references);
                }
            }
        }
    }

    private static string? FindEntity(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("SourceRef", out var sourceRef) &&
                sourceRef.ValueKind == JsonValueKind.Object &&
                sourceRef.TryGetProperty("Entity", out var entity) &&
                entity.ValueKind == JsonValueKind.String)
            {
                return entity.GetString();
            }

            foreach (var child in element.EnumerateObject())
            {
                var nestedEntity = FindEntity(child.Value);
                if (nestedEntity is not null)
                {
                    return nestedEntity;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nestedEntity = FindEntity(child);
                if (nestedEntity is not null)
                {
                    return nestedEntity;
                }
            }
        }

        return null;
    }

    private static void AddQueryReference(
        string value,
        string? kind,
        HashSet<PowerBiReportFieldReference> references)
    {
        if (value.Contains('.'))
        {
            var separator = value.LastIndexOf('.');
            if (separator > 0 && separator < value.Length - 1)
            {
                references.Add(new PowerBiReportFieldReference(
                    value[..separator].Trim('\'', '"', ' '),
                    value[(separator + 1)..].Trim('\'', '"', ' '),
                    kind,
                    value));
            }
        }
        else
        {
            references.Add(new PowerBiReportFieldReference(null, value, kind, value));
        }
    }
}
