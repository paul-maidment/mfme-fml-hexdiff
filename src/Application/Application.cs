using System;
using System.Diagnostics;
using System.IO;
using MfmeFmlDecoder.Decoder;

namespace MfmeFmlDecoder.Application
{
    internal sealed class Application
    {
        public Application()
        {
        }

        public int Run(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            string fileName = args[0];
            uint offset = (args.Length > 1) ? ParseOffset(args[1]) : 0;

            string resolvedInputPath = fileName;
            string tempDirToCleanup = null;

            try
            {
                resolvedInputPath = ResolveInputToDat(fileName, out tempDirToCleanup);

                var fileWalker = new FileWalker();
                string cleanedOutputPath = BuildCleanedOutputPath(fileName);
                FileWalker.TlvWalkResult cleanResult = fileWalker.WriteCleanedFileWithoutTag(
                    resolvedInputPath,
                    cleanedOutputPath,
                    offset,
                    tagToRemove: 0x97
                );

                Console.WriteLine($"Cleaned TLV written: {cleanResult.OutputPath}");
                Console.WriteLine($"TLV records parsed: {cleanResult.TotalTlvRecords}");
                Console.WriteLine($"Tag 0x97 removed: {cleanResult.RemovedRecords}");

                return 0;
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"Error: File not found - {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempDirToCleanup))
                {
                    TryDeleteDirectory(tempDirToCleanup);
                }
            }
        }

        private static uint ParseOffset(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToUInt32(s.Substring(2), 16);
            }
            return uint.Parse(s);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  MfmeFmlDecoder <filename.(dat|fml)> [offset]");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  - Default mode writes <input>_cleaned.dat with TLV tag 0x97 removed.");
            Console.WriteLine("  - Offset can be decimal or 0xHEX and is applied to both files.");
        }

        private static string BuildCleanedOutputPath(string inputPath)
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            string outputDirectory = Path.GetDirectoryName(fullInputPath) ?? Directory.GetCurrentDirectory();
            string inputNameWithoutExtension = Path.GetFileNameWithoutExtension(fullInputPath);
            return Path.Combine(outputDirectory, $"{inputNameWithoutExtension}_cleaned.dat");
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

            using (var proc = Process.Start(psi))
            {
                if (proc == null)
                {
                    throw new InvalidOperationException("Failed to start mfme_decryptor.exe process.");
                }

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();

                // Plenty for local decrypt/unpack; avoids hanging forever.
                if (!proc.WaitForExit(60_000))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    throw new TimeoutException("mfme_decryptor.exe timed out after 60 seconds.");
                }

                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"mfme_decryptor.exe failed with exit code {proc.ExitCode}.{Environment.NewLine}" +
                        (string.IsNullOrWhiteSpace(stdout) ? "" : $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}") +
                        (string.IsNullOrWhiteSpace(stderr) ? "" : $"stderr:{Environment.NewLine}{stderr}{Environment.NewLine}")
                    );
                }
            }
        }

        private static string FindMfmeDecoderExe()
        {
            // Try common locations:
            // - next to build output: <base>/lib/mfme_decryptor.exe
            // - current working directory: <cwd>/lib/mfme_decryptor.exe
            // - walk upwards from base dir a few levels looking for lib/mfme_decryptor.exe (useful when running from bin/Debug/...).
            string[] directCandidates =
            {
                Path.Combine(AppContext.BaseDirectory, "lib", "mfme_decryptor.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "lib", "mfme_decryptor.exe")
            };

            foreach (var c in directCandidates)
            {
                if (File.Exists(c)) return c;
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "lib", "mfme_decryptor.exe");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
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
}
