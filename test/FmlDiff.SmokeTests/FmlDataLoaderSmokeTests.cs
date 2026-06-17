using System;
using System.IO;
using FmlDiff.Services;
using Xunit;

namespace FmlDiff.SmokeTests;

public sealed class FmlDataLoaderSmokeTests
{
    private const string DefaultFmlPath = @"d:\SampleFML\multi_chunk-samples\multi_chunk_large_sample.fml";
    private const string DefaultDecodedPath = @"d:\SampleFML\multi_chunk-samples\multi_chunk_large_sample_decoded.dat";

    [Fact]
    public void LoadCleanedBytes_round_trips_multi_chunk_sample_without_tlv_eof()
    {
        string fmlPath = Environment.GetEnvironmentVariable("FMLDIFF_SMOKE_FML") ?? DefaultFmlPath;
        string decodedPath = Environment.GetEnvironmentVariable("FMLDIFF_SMOKE_DECODED") ?? DefaultDecodedPath;

        Assert.True(File.Exists(fmlPath), $"Smoke test FML not found at '{fmlPath}'.");
        Assert.True(File.Exists(decodedPath), $"Smoke test decoded DAT not found at '{decodedPath}'.");

        var loader = new FmlDataLoader();
        byte[] cleaned = loader.LoadCleanedBytes(fmlPath);
        byte[] expectedCleaned = loader.LoadCleanedBytes(decodedPath);

        Assert.Equal(expectedCleaned.Length, cleaned.Length);
        Assert.Equal(expectedCleaned, cleaned);
    }
}
