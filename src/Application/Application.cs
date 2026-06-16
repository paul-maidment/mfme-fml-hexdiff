using System;
using System.IO;
using FmlDiff.Decryption;
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

            try
            {
                byte[] datBytes = LoadDecodedBytes(fileName);

                var fileWalker = new FileWalker();
                string cleanedOutputPath = BuildCleanedOutputPath(fileName);
                FileWalker.TlvWalkResult cleanResult = fileWalker.WriteCleanedFileWithoutTag(
                    datBytes,
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

        private static byte[] LoadDecodedBytes(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException("Input path is empty.");
            }

            string fullInputPath = Path.GetFullPath(inputPath);
            if (!File.Exists(fullInputPath))
            {
                throw new FileNotFoundException(fullInputPath);
            }

            byte[] fileBytes = File.ReadAllBytes(fullInputPath);
            string ext = Path.GetExtension(fullInputPath);
            if (string.Equals(ext, ".fml", StringComparison.OrdinalIgnoreCase))
            {
                return FmlDecryptor.Decrypt(fileBytes);
            }

            return fileBytes;
        }
    }
}
