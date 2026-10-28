using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace BISentinel.Api.Reviews;

public sealed class PbiToolsExtractor(IOptionsMonitor<ReviewOptions> optionsMonitor)
{
    private const long MaximumExtractedBytes = 512 * 1024 * 1024;
    private const int MaximumExtractedFiles = 20_000;
    private static readonly TimeSpan ExtractionTimeout = TimeSpan.FromMinutes(3);

    public async Task ExtractAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var configuredPath = optionsMonitor.CurrentValue.PbiToolsPath;
        var executablePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "tools", "pbi-tools.exe")
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (!File.Exists(executablePath))
        {
            throw new InvalidDataException(
                $"PBIT review requires pbi-tools, but the executable was not found at '{executablePath}'. " +
                "The deployment must include tools\\pbi-tools.exe or Review:PbiToolsPath must point to an installed executable.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(destinationPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("extract");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-extractFolder");
        startInfo.ArgumentList.Add(destinationPath);
        startInfo.ArgumentList.Add("-modelSerialization");
        startInfo.ArgumentList.Add("Tmdl");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidDataException("The configured pbi-tools executable could not be started.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException(
                "Could not start pbi-tools. Verify the bundled executable or Review:PbiToolsPath and that its runtime is available.",
                exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExtractionTimeout);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutput, standardError);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new InvalidDataException("pbi-tools extraction exceeded the three-minute time limit.");
        }

        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? output : error;
            if (detail.Length > 1000)
            {
                detail = detail[..1000];
            }

            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(detail)
                    ? "pbi-tools could not extract this Power BI file."
                    : $"pbi-tools could not extract this Power BI file: {detail.Trim()}");
        }

        if (!Directory.Exists(destinationPath))
        {
            throw new InvalidDataException("pbi-tools completed without creating an extraction folder.");
        }

        long extractedBytes = 0;
        var extractedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(destinationPath, "*", SearchOption.AllDirectories))
        {
            extractedFiles++;
            if (extractedFiles > MaximumExtractedFiles)
            {
                throw new InvalidDataException("pbi-tools extracted too many files.");
            }

            var fileSize = new FileInfo(file).Length;
            if (fileSize > MaximumExtractedBytes - extractedBytes)
            {
                throw new InvalidDataException("pbi-tools output exceeds the 512 MB safety limit.");
            }

            extractedBytes += fileSize;
        }
    }
}
