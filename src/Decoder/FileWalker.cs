using System;
using System.Collections.Generic;
using System.IO;

namespace MfmeFmlDecoder.Decoder
{
    internal sealed class FileWalker
    {
        private const uint TlvTerminationTag = 0xFFFFFFFF;
        private const uint TagWithExtraData = 0x43;
        private const int ExtraDataLengthAfterTag43 = 0x303;

        internal sealed record TlvWalkResult(
            int TotalTlvRecords,
            int RemovedRecords,
            string OutputPath
        );

        private sealed record TlvRecord(
            uint Tag,
            byte[] Value,
            byte[] TrailingBytes
        );

        public byte[] GetCleanedBytesWithoutTag(string inputDatPath, uint offset, uint tagToRemove)
        {
            if (string.IsNullOrWhiteSpace(inputDatPath))
            {
                throw new ArgumentException("Input path is empty.", nameof(inputDatPath));
            }

            string fullInputPath = Path.GetFullPath(inputDatPath);
            if (!File.Exists(fullInputPath))
            {
                throw new FileNotFoundException(fullInputPath);
            }

            using FileStream fileStream = new FileStream(fullInputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return GetCleanedBytesWithoutTag(fileStream, offset, tagToRemove);
        }

        public byte[] GetCleanedBytesWithoutTag(byte[] inputBytes, uint offset, uint tagToRemove)
        {
            if (inputBytes == null || inputBytes.Length == 0)
            {
                throw new ArgumentException("Input data is empty.", nameof(inputBytes));
            }

            using MemoryStream inputStream = new MemoryStream(inputBytes, writable: false);
            return GetCleanedBytesWithoutTag(inputStream, offset, tagToRemove);
        }

        private byte[] GetCleanedBytesWithoutTag(Stream inputStream, uint offset, uint tagToRemove)
        {
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            WriteFilteredRecords(inputStream, offset, tagToRemove, writer);
            return output.ToArray();
        }

        private static void WriteFilteredRecords(Stream inputStream, uint offset, uint tagToRemove, BinaryWriter writer)
        {
            bool foundTerminationTag = false;

            using BinaryReader reader = new BinaryReader(inputStream, System.Text.Encoding.UTF8, leaveOpen: true);

            if (offset > inputStream.Length)
            {
                throw new InvalidOperationException(
                    $"Offset 0x{offset:X} is beyond file length 0x{inputStream.Length:X}."
                );
            }

            if (offset > 0)
            {
                byte[] prefixBytes = reader.ReadBytes(checked((int)offset));
                writer.Write(prefixBytes);
            }

            while (inputStream.Position < inputStream.Length)
            {
                long recordStartOffset = inputStream.Position;
                uint tag = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                byte[] values = reader.ReadBytes(checked((int)length));
                if (values.Length != length)
                {
                    throw new EndOfStreamException(
                        $"Unexpected EOF reading tag 0x{tag:X2} at offset 0x{recordStartOffset:X8}. " +
                        $"Expected {length} bytes but only {values.Length} bytes were read."
                    );
                }

                byte[] trailingBytes = Array.Empty<byte>();
                if (tag == TagWithExtraData)
                {
                    trailingBytes = reader.ReadBytes(ExtraDataLengthAfterTag43);
                    if (trailingBytes.Length != ExtraDataLengthAfterTag43)
                    {
                        throw new EndOfStreamException(
                            $"Unexpected EOF reading trailing bytes for tag 0x43 at offset 0x{recordStartOffset:X8}."
                        );
                    }
                }

                if (tag != tagToRemove)
                {
                    writer.Write(tag);
                    writer.Write(length);
                    writer.Write(values);

                    if (trailingBytes.Length > 0)
                        writer.Write(trailingBytes);
                }

                if (tag == TlvTerminationTag)
                {
                    foundTerminationTag = true;
                    break;
                }
            }

            if (!foundTerminationTag)
            {
                throw new InvalidOperationException(
                    "Could not locate TLV termination tag 0xFFFFFFFF; cannot identify components section."
                );
            }

            long remaining = inputStream.Length - inputStream.Position;
            if (remaining > 0)
            {
                byte[] componentsBytes = reader.ReadBytes(checked((int)remaining));
                writer.Write(componentsBytes);
            }
        }

        public TlvWalkResult WriteCleanedFileWithoutTag(string inputDatPath, string outputPath, uint offset, uint tagToRemove)
        {
            if (string.IsNullOrWhiteSpace(inputDatPath))
            {
                throw new ArgumentException("Input path is empty.", nameof(inputDatPath));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is empty.", nameof(outputPath));
            }

            string fullInputPath = Path.GetFullPath(inputDatPath);
            string fullOutputPath = Path.GetFullPath(outputPath);

            if (!File.Exists(fullInputPath))
            {
                throw new FileNotFoundException(fullInputPath);
            }

            using FileStream fileStream = new FileStream(fullInputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return WriteCleanedFileWithoutTag(fileStream, fullOutputPath, offset, tagToRemove);
        }

        public TlvWalkResult WriteCleanedFileWithoutTag(byte[] inputBytes, string outputPath, uint offset, uint tagToRemove)
        {
            if (inputBytes == null || inputBytes.Length == 0)
            {
                throw new ArgumentException("Input data is empty.", nameof(inputBytes));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is empty.", nameof(outputPath));
            }

            string fullOutputPath = Path.GetFullPath(outputPath);
            using MemoryStream inputStream = new MemoryStream(inputBytes, writable: false);
            return WriteCleanedFileWithoutTag(inputStream, fullOutputPath, offset, tagToRemove);
        }

        private TlvWalkResult WriteCleanedFileWithoutTag(Stream inputStream, string fullOutputPath, uint offset, uint tagToRemove)
        {
            int removed = 0;
            byte[] prefixBytes;
            List<TlvRecord> records;
            byte[] componentsBytes;
            ReadRecords(inputStream, offset, out prefixBytes, out records, out componentsBytes);

            using (FileStream outputStream = new FileStream(fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(outputStream))
            {
                if (prefixBytes.Length > 0)
                {
                    writer.Write(prefixBytes);
                }

                foreach (TlvRecord record in records)
                {
                    if (record.Tag == tagToRemove)
                    {
                        removed++;
                        continue;
                    }

                    writer.Write(record.Tag);
                    writer.Write((uint)record.Value.Length);
                    writer.Write(record.Value);

                    if (record.TrailingBytes.Length > 0)
                    {
                        writer.Write(record.TrailingBytes);
                    }
                }

                if (componentsBytes.Length > 0)
                {
                    writer.Write(componentsBytes);
                }
            }

            return new TlvWalkResult(records.Count, removed, fullOutputPath);
        }

        private static void ReadRecords(Stream inputStream, uint offset, out byte[] prefixBytes, out List<TlvRecord> records, out byte[] componentsBytes)
        {
            records = new List<TlvRecord>();
            bool foundTerminationTag = false;

            using BinaryReader reader = new BinaryReader(inputStream, System.Text.Encoding.UTF8, leaveOpen: true);

            if (offset > inputStream.Length)
            {
                throw new InvalidOperationException(
                    $"Offset 0x{offset:X} is beyond file length 0x{inputStream.Length:X}."
                );
            }

            prefixBytes = reader.ReadBytes(checked((int)offset));

            while (inputStream.Position < inputStream.Length)
            {
                long recordStartOffset = inputStream.Position;
                uint tag = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                byte[] values = reader.ReadBytes(checked((int)length));
                if (values.Length != length)
                {
                    throw new EndOfStreamException(
                        $"Unexpected EOF reading tag 0x{tag:X2} at offset 0x{recordStartOffset:X8}. " +
                        $"Expected {length} bytes but only {values.Length} bytes were read."
                    );
                }

                byte[] trailingBytes = Array.Empty<byte>();
                if (tag == TagWithExtraData)
                {
                    trailingBytes = reader.ReadBytes(ExtraDataLengthAfterTag43);
                    if (trailingBytes.Length != ExtraDataLengthAfterTag43)
                    {
                        throw new EndOfStreamException(
                            $"Unexpected EOF reading trailing bytes for tag 0x43 at offset 0x{recordStartOffset:X8}."
                        );
                    }
                }

                records.Add(new TlvRecord(tag, values, trailingBytes));

                if (tag == TlvTerminationTag)
                {
                    foundTerminationTag = true;
                    break;
                }
            }

            if (!foundTerminationTag)
            {
                throw new InvalidOperationException(
                    "Could not locate TLV termination tag 0xFFFFFFFF; cannot identify components section."
                );
            }

            long remaining = inputStream.Length - inputStream.Position;
            componentsBytes = reader.ReadBytes(checked((int)remaining));
        }
    }
}