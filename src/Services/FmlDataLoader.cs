using System;
using System.Diagnostics;
using System.IO;
using MfmeFmlDecoder.Decoder;

namespace FmlDiff.Services;

public sealed class FmlDataLoader
{
    private readonly FileWalker _fileWalker = new();

    public byte[] LoadCleanedBytes(string inputPath, uint offset = 0, uint tagToRemove = 0x97)
    {
        string resolvedInputPath = inputPath;
        string tempDirToCleanup = null;

        try
        {
            resolvedInputPath = ResolveInputToDat(inputPath, out tempDirToCleanup);
            return _fileWalker.GetCleanedBytesWithoutTag(resolvedInputPath, offset, tagToRemove);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempDirToCleanup))
            {
                TryDeleteDirectory(tempDirToCleanup);
            }
        }
    }

    private static string ResolveInputToDat(string inputPath, out string tempDirToCleanup)
    {
        tempDirToCleanup = null;

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException("Input path is empty.");
        }

        string fullInputPath = Path.GetFullPath(inputPath);

        if (!File.Exists(fullInputPath))
        {
            throw new FileNotFoundException(fullInputPath);
        }

        string ext = Path.GetExtension(fullInputPath);
        if (!string.Equals(ext, ".fml", StringComparison.OrdinalIgnoreCase))
        {
            return fullInputPath;
        }

        string decoderExePath = FindMfmeDecoderExe();
        if (decoderExePath == null)
        {
            throw new FileNotFoundException("Could not locate lib/mfme_decryptor.exe. Expected it relative to the app folder or current working directory.");
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "MfmeFmlDecoder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        tempDirToCleanup = tempDir;

        string tempFmlName = Path.GetFileName(fullInputPath);
        string tempFmlPath = Path.Combine(tempDir, tempFmlName);
        File.Copy(fullInputPath, tempFmlPath, overwrite: true);

        string expectedDatPath = Path.Combine(
            tempDir,
            Path.GetFileNameWithoutExtension(tempFmlName) + "_decoded.dat"
        );

        RunDecoder(decoderExePath, tempFmlPath, tempDir);

        if (!File.Exists(expectedDatPath))
        {
            throw new FileNotFoundException($"Decoded .dat not found at expected path: {expectedDatPath}");
        }

        return expectedDatPath;
    }

    private static void RunDecoder(string decoderExePath, string fmlPath, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = decoderExePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add(fmlPath);

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            throw new InvalidOperationException("Failed to start mfme_decryptor.exe process.");
        }

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();

        if (!proc.WaitForExit(60_000))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort only.
            }

            throw new TimeoutException("mfme_decryptor.exe timed out after 60 seconds.");
        }

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"mfme_decryptor.exe failed with exit code {proc.ExitCode}.{Environment.NewLine}" +
                (string.IsNullOrWhiteSpace(stdout) ? string.Empty : $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}") +
                (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $"stderr:{Environment.NewLine}{stderr}{Environment.NewLine}")
            );
        }
    }

    private static string FindMfmeDecoderExe()
    {
        string[] directCandidates =
        {
            Path.Combine(AppContext.BaseDirectory, "lib", "mfme_decryptor.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "lib", "mfme_decryptor.exe")
        };

        foreach (string candidate in directCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && directory != null; i++)
        {
            string candidate = Path.Combine(directory.FullName, "lib", "mfme_decryptor.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
