using System;
using System.IO;
using FmlDiff.Decryption;
using MfmeFmlDecoder.Decoder;

namespace FmlDiff.Services;

public sealed class FmlDataLoader
{
    private readonly FileWalker _fileWalker = new();

    public byte[] LoadCleanedBytes(string inputPath, uint offset = 0, uint tagToRemove = 0x97, IProgress<double> progress = null)
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

        progress?.Report(0);

        byte[] datBytes = LoadDecodedBytes(fullInputPath, progress);
        progress?.Report(0.95);

        byte[] cleanedBytes = _fileWalker.GetCleanedBytesWithoutTag(datBytes, offset, tagToRemove);
        progress?.Report(1);

        return cleanedBytes;
    }

    private static byte[] LoadDecodedBytes(string fullInputPath, IProgress<double> progress)
    {
        string ext = Path.GetExtension(fullInputPath);
        byte[] fileBytes = File.ReadAllBytes(fullInputPath);
        progress?.Report(0.05);

        if (string.Equals(ext, ".fml", StringComparison.OrdinalIgnoreCase))
        {
            return FmlDecryptor.Decrypt(fileBytes, MapProgress(progress, 0.05, 0.90));
        }

        progress?.Report(0.90);
        return fileBytes;
    }

    private static IProgress<double> MapProgress(IProgress<double> progress, double start, double end)
    {
        if (progress == null)
            return null;

        return new Progress<double>(value =>
        {
            progress.Report(start + value * (end - start));
        });
    }
}
