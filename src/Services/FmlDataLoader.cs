using System;
using System.IO;
using FmlDiff.Decryption;
using MfmeFmlDecoder.Decoder;

namespace FmlDiff.Services;

public sealed class FmlDataLoader
{
    private readonly FileWalker _fileWalker = new();

    public byte[] LoadCleanedBytes(string inputPath, uint offset = 0, uint tagToRemove = 0x97)
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

        byte[] datBytes = LoadDecodedBytes(fullInputPath);
        return _fileWalker.GetCleanedBytesWithoutTag(datBytes, offset, tagToRemove);
    }

    private static byte[] LoadDecodedBytes(string fullInputPath)
    {
        string ext = Path.GetExtension(fullInputPath);
        byte[] fileBytes = File.ReadAllBytes(fullInputPath);

        if (string.Equals(ext, ".fml", StringComparison.OrdinalIgnoreCase))
        {
            return FmlDecryptor.Decrypt(fileBytes);
        }

        return fileBytes;
    }
}
