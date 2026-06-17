using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FmlDiff.Decryption
{
    internal static class FmlDecryptor
    {
        public static byte[] Decrypt(byte[] fmlBytes, IProgress<double> progress = null)
        {
            if (fmlBytes == null || fmlBytes.Length < 148)
                throw new ArgumentException("FML data too short");

            string password = DerivePassword(fmlBytes);

            try
            {
                return DecryptAllChunks(fmlBytes, password, progress);
            }
            catch (Exception)
            {
                progress?.Report(0);
                return DecryptAllChunks(fmlBytes, AltPassword, progress);
            }
        }

        // Two file format phases exist:
        //   Phase 1 (legacy): static password "b67de657", last chunk's next_header_offset == EOF (no seed)
        //   Phase 2 (current): 4-byte seed at EOF, last chunk's next_header_offset == EOF - 4
        // We detect the phase by walking the chunk header chain and comparing the final
        // next_header_offset to the file length.
        private static string DerivePassword(byte[] fmlBytes)
        {
            uint lastNextOff = FindLastNextHeaderOffset(fmlBytes);

            if (lastNextOff == fmlBytes.Length)
                return AltPassword;

            if (lastNextOff == fmlBytes.Length - 4)
            {
                uint seed = BitConverter.ToUInt32(fmlBytes, fmlBytes.Length - 4);
                return Mix(seed).ToString("x8");
            }

            uint last4 = BitConverter.ToUInt32(fmlBytes, fmlBytes.Length - 4);
            return Mix(last4).ToString("x8");
        }

        // Walks the chunk header chain from offset 128, following each chunk's
        // next_header_offset field, and returns the final chunk's next_header_offset.
        private static uint FindLastNextHeaderOffset(byte[] fmlBytes)
        {
            int pos = 128;
            uint lastNextOff = 0;
            int maxChunks = 10000;

            while (pos + 16 <= fmlBytes.Length && maxChunks-- > 0)
            {
                uint csize = BitConverter.ToUInt32(fmlBytes, pos);
                uint nextOff = BitConverter.ToUInt32(fmlBytes, pos + 12);
                lastNextOff = nextOff;

                if (nextOff >= fmlBytes.Length || nextOff <= pos)
                    break;

                if (nextOff + 16 > fmlBytes.Length)
                    break;

                pos = (int)nextOff;
            }

            return lastNextOff;
        }

        private static byte[] DecryptAllChunks(byte[] fmlBytes, string password, IProgress<double> progress)
        {
            byte[] aesKey = Ripemd128.ComputeHash(Encoding.ASCII.GetBytes(password));
            byte[] iv = EncryptEcb(aesKey, s_allFfBlock);
            uint initialDsize = BitConverter.ToUInt32(fmlBytes, 132);

            using (Aes aes = Aes.Create())
            using (MemoryStream output = new MemoryStream(Math.Max((int)initialDsize, 4096)))
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = aesKey;

                uint csize = BitConverter.ToUInt32(fmlBytes, 128);
                uint dsize = initialDsize;
                uint expectedCrc32 = BitConverter.ToUInt32(fmlBytes, 136);
                int position = 144;
                int chunkIndex = 0;
                int payloadEnd = Math.Max(fmlBytes.Length - 4, 144);
                int payloadSpan = Math.Max(payloadEnd - 144, 1);

                while (position < fmlBytes.Length - 4)
                {
                    int ctLen = (int)Math.Min(csize, (uint)(fmlBytes.Length - position));
                    byte[] compressed = EclDecrypt(fmlBytes, position, ctLen, aes, iv);
                    int writeLen = ZlibDecompressTo(output, compressed, dsize);

                    if (writeLen > 0)
                    {
                        long chunkStart = output.Length - writeLen;
                        if (output.TryGetBuffer(out ArraySegment<byte> segment))
                        {
                            uint actualCrc32 = ComputeChunkCrc32(segment.Array, (int)chunkStart + segment.Offset, writeLen);
                            if (actualCrc32 != expectedCrc32)
                                Console.Error.WriteLine($"WARNING: chunk {chunkIndex} CRC32 mismatch (expected 0x{expectedCrc32:X8}, computed 0x{actualCrc32:X8})");
                        }
                    }

                    position += ctLen;

                    if (position + 16 > fmlBytes.Length - 4)
                        break;

                    csize = BitConverter.ToUInt32(fmlBytes, position);
                    dsize = BitConverter.ToUInt32(fmlBytes, position + 4);
                    expectedCrc32 = BitConverter.ToUInt32(fmlBytes, position + 8);
                    position += 16;
                    chunkIndex++;

                    progress?.Report(Math.Min(1.0, (double)(position - 144) / payloadSpan));
                }

                progress?.Report(1.0);
                return output.ToArray();
            }
        }

        private static uint Mix(uint last4)
        {
            uint result = last4;
            const uint addr = 0x00C59A00;
            for (int i = 0; i < 100; i++)
            {
                uint x = addr ^ result;
                uint v4 = (x >> 24) | (x << 24) | (x & 0x00FFFF00);
                result = (v4 << 3) | (v4 >> 29);
            }
            return result;
        }

        private static byte[] EncryptEcb(byte[] key, byte[] plaintext)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] output = new byte[plaintext.Length];
                    encryptor.TransformBlock(plaintext, 0, plaintext.Length, output, 0);
                    return output;
                }
            }
        }

        private static byte[] EclDecrypt(byte[] source, int offset, int length, Aes aes, byte[] iv)
        {
            byte[] fb_d = new byte[16];
            byte[] fb_b = new byte[16];
            Array.Copy(iv, fb_d, 16);

            int fullBlocks = length - (length % 16);
            byte[] result = new byte[fullBlocks + (length % 16)];
            int resultPos = 0;

            using (ICryptoTransform decryptor = aes.CreateDecryptor())
            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            {
                for (int i = 0; i < fullBlocks; i += 16)
                {
                    int sourceIndex = offset + i;
                    for (int j = 0; j < 16; j++)
                        fb_b[j] = (byte)(source[sourceIndex + j] ^ fb_d[j]);

                    byte[] dec = new byte[16];
                    decryptor.TransformBlock(source, sourceIndex, 16, dec, 0);

                    for (int j = 0; j < 16; j++)
                        result[resultPos++] = (byte)(dec[j] ^ fb_d[j]);

                    byte[] tmp = fb_d;
                    fb_d = fb_b;
                    fb_b = tmp;
                }

                int rem = length % 16;
                if (rem > 0)
                {
                    byte[] keystream = new byte[16];
                    encryptor.TransformBlock(fb_d, 0, 16, keystream, 0);

                    int tailStart = offset + fullBlocks;
                    for (int j = 0; j < rem; j++)
                        result[resultPos++] = (byte)(source[tailStart + j] ^ keystream[j]);
                }
            }

            return result;
        }

        private static int ZlibDecompressTo(MemoryStream output, byte[] compressed, uint maxOutputSize)
        {
            long startLength = output.Length;
            using (MemoryStream compressedStream = new MemoryStream(compressed, writable: false))
            using (ZLibStream zlib = new ZLibStream(compressedStream, CompressionMode.Decompress))
            {
                byte[] buffer = new byte[65536];
                int bytesRead;
                while ((bytesRead = zlib.Read(buffer, 0, buffer.Length)) > 0)
                {
                    int allowed = bytesRead;
                    if (maxOutputSize > 0)
                    {
                        long written = output.Length - startLength;
                        long remaining = maxOutputSize - written;
                        if (remaining <= 0)
                            break;

                        allowed = (int)Math.Min(remaining, bytesRead);
                    }

                    output.Write(buffer, 0, allowed);
                    if (maxOutputSize > 0 && output.Length - startLength >= maxOutputSize)
                        break;
                }
            }

            return checked((int)(output.Length - startLength));
        }

        private static uint ComputeChunkCrc32(byte[] buffer, int offset, int length)
        {
            uint crc = 0;
            int end = offset + length;
            for (int i = offset; i < end; i++)
            {
                byte b = buffer[i];
                crc ^= b;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return crc;
        }

        private const string AltPassword = "b67de657";

        private static readonly byte[] s_allFfBlock = new byte[16]
        {
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF
        };
    }
}
