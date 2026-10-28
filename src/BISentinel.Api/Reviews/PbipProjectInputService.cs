using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace BISentinel.Api.Reviews;

public sealed class PbipProjectInputService(
    IPbipProjectReviewer reviewer,
    PbiToolsExtractor pbiToolsExtractor)
{
    private const long MaximumUploadBytes = 500L * 1024 * 1024;
    private const long MaximumExpandedBytes = 512 * 1024 * 1024;
    private const int MaximumArchiveEntries = 20_000;
    public async Task<PbipReviewResult> ReviewArchiveAsync(
        IFormFile archiveFile,
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (archiveFile.Length <= 0 || archiveFile.Length > MaximumUploadBytes)
        {
            throw new InvalidDataException("The Power BI file or ZIP must be between 1 byte and 500 MB.");
        }

        var extension = Path.GetExtension(archiveFile.FileName);
        if (extension.Equals(".pbit", StringComparison.OrdinalIgnoreCase))
        {
            return await ReviewTemplateUploadAsync(archiveFile, projectId, cancellationToken);
        }

        if (extension.Equals(".pbix", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("PBIX files are not supported. Upload a complete PBIP project ZIP or a PBIT file.");
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Upload a complete PBIP/PBIR project ZIP or a .pbit template.");
        }

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "BISentinel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            await using var archiveStream = archiveFile.OpenReadStream();
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaximumArchiveEntries)
            {
                throw new InvalidDataException("The project ZIP contains too many entries.");
            }

            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = GetArchiveDestination(temporaryRoot, entry);
                var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                if (isDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (entry.Length > MaximumExpandedBytes - expandedBytes)
                {
                    throw new InvalidDataException("The project ZIP expands beyond the 512 MB safety limit.");
                }
                expandedBytes += entry.Length;

                var parentDirectory = Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidDataException("The project ZIP contains an invalid file path.");
                Directory.CreateDirectory(parentDirectory);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous);
                var copiedBytes = await CopyEntryAsync(
                    input,
                    output,
                    MaximumExpandedBytes - (expandedBytes - entry.Length),
                    cancellationToken);
                if (copiedBytes != entry.Length)
                {
                    throw new InvalidDataException("A file in the project ZIP has an invalid declared size.");
                }
            }

            var projectFiles = Directory.GetFiles(temporaryRoot, "*.pbip", SearchOption.AllDirectories);
            if (projectFiles.Length > 1)
            {
                throw new InvalidDataException("The uploaded ZIP must contain only one PBIP project.");
            }

            if (projectFiles.Length == 1)
            {
                var projectRoot = Path.GetDirectoryName(projectFiles[0])
                    ?? throw new InvalidDataException("The PBIP project folder could not be resolved.");
                return await reviewer.ReviewAsync(
                    projectRoot,
                    ResolveProjectId(projectId, projectFiles[0]),
                    null,
                    cancellationToken);
            }

            var reportDescriptors = Directory.GetFiles(temporaryRoot, "definition.pbir", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var reportRoot = Path.GetDirectoryName(path)!;
                    return File.Exists(Path.Combine(reportRoot, "definition", "report.json")) &&
                        File.Exists(Path.Combine(reportRoot, "definition", "pages", "pages.json"));
                })
                .ToArray();
            if (reportDescriptors.Length != 1)
            {
                throw new InvalidDataException(
                    "Upload a ZIP containing one complete PBIP project (including all referenced report and semantic-model folders) or one standalone PBIR report.");
            }

            var pbirRoot = Path.GetDirectoryName(reportDescriptors[0])
                ?? throw new InvalidDataException("The standalone PBIR report folder could not be resolved.");
            var projectName = Path.GetFileNameWithoutExtension(archiveFile.FileName);
            return await reviewer.ReviewAsync(
                pbirRoot,
                ResolveProjectId(projectId, archiveFile.FileName),
                projectName,
                cancellationToken);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private async Task<PbipReviewResult> ReviewTemplateUploadAsync(
        IFormFile file,
        string? projectId,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "BISentinel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var inputPath = Path.Combine(
                temporaryRoot,
                Path.GetFileName(file.FileName));
            await using (var output = new FileStream(
                inputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous))
            {
                await file.CopyToAsync(output, cancellationToken);
            }

            return await ExtractAndReviewAsync(inputPath, projectId, cancellationToken);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private async Task<PbipReviewResult> ExtractAndReviewAsync(
        string inputPath,
        string? projectId,
        CancellationToken cancellationToken,
        string? temporaryRoot = null)
    {
        temporaryRoot ??= Path.GetDirectoryName(inputPath)
            ?? throw new InvalidDataException("The uploaded Power BI file could not be located.");
        var extractedPath = Path.Combine(temporaryRoot, "extracted");
        await pbiToolsExtractor.ExtractAsync(inputPath, extractedPath, cancellationToken);

        return await reviewer.ReviewAsync(
            extractedPath,
            ResolveProjectId(projectId, inputPath),
            Path.GetFileNameWithoutExtension(inputPath),
            cancellationToken);
    }

    private static string GetArchiveDestination(string temporaryRoot, ZipArchiveEntry entry)
    {
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixFileType == 0xA000)
        {
            throw new InvalidDataException("Symbolic links are not allowed in a project ZIP.");
        }

        var normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var pathSegments = normalizedName
            .TrimEnd(Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedName) ||
            normalizedName.Contains(':') ||
            pathSegments.Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("The project ZIP contains a path outside its project folder.");
        }

        var root = Path.GetFullPath(temporaryRoot);
        var destination = Path.GetFullPath(Path.Combine(root, normalizedName));
        EnsureWithinRoot(destination, root);
        return destination;
    }

    private static void EnsureWithinRoot(string path, string root)
    {
        var relativePath = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The selected project path must remain within the allowed project root.");
        }
    }

    private static async Task<long> CopyEntryAsync(
        Stream input,
        Stream output,
        long remainingLimit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long copiedBytes = 0;
        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                return copiedBytes;
            }

            copiedBytes += bytesRead;
            if (copiedBytes > remainingLimit)
            {
                throw new InvalidDataException("The project ZIP expands beyond the 512 MB safety limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }
    }

    private static string ResolveProjectId(string? projectId, string projectDescriptorPath)
    {
        var identifier = string.IsNullOrWhiteSpace(projectId)
            ? Path.GetFileNameWithoutExtension(projectDescriptorPath)
            : projectId.Trim();
        if (identifier.Length > 100 ||
            !Regex.IsMatch(identifier, "^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$"))
        {
            throw new InvalidDataException(
                "Project ID must be 1-100 letters, numbers, dots, underscores, or hyphens and start with a letter or number.");
        }

        return identifier;
    }
}
