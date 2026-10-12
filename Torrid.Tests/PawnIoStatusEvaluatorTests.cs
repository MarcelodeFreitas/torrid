using Torrid.Core.Drivers;

namespace Torrid.Tests;

public class PawnIoStatusEvaluatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public void MissingOrBlankVersion_IsNotInstalled(string? version)
    {
        var status = PawnIoStatusEvaluator.Evaluate(version, detectionFailed: false);

        Assert.Equal(PawnIoState.NotInstalled, status.State);
        Assert.Null(status.Version);
    }

    [Theory]
    [InlineData("2.1.0")]
    [InlineData("2.2.0")]
    [InlineData("2.2.0.0")]
    [InlineData("2.1.0.0")]
    [InlineData("3.0")]
    [InlineData("10.0.1")]
    public void MinimumOrNewer_IsCurrent_AndKeepsTheVersionText(string version)
    {
        var status = PawnIoStatusEvaluator.Evaluate(version, detectionFailed: false);

        Assert.Equal(PawnIoState.Current, status.State);
        Assert.Equal(version, status.Version);
    }

    [Theory]
    [InlineData("2.0.1.0")]
    [InlineData("2.0.1")]
    [InlineData("2.0.9.9")]
    [InlineData("1.9")]
    [InlineData("0.5.0")]
    public void OlderThanMinimum_IsOutdated(string version)
    {
        var status = PawnIoStatusEvaluator.Evaluate(version, detectionFailed: false);

        Assert.Equal(PawnIoState.Outdated, status.State);
        Assert.Equal(version, status.Version);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("v2.2.0")]
    [InlineData("2.x.0")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.0")]
    [InlineData("2")]
    public void UnparsableVersion_IsUnknown(string version)
    {
        var status = PawnIoStatusEvaluator.Evaluate(version, detectionFailed: false);

        Assert.Equal(PawnIoState.Unknown, status.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.2.0")]
    [InlineData("garbage")]
    public void DetectionFailure_IsUnknown_WhateverTheVersion(string? version)
    {
        var status = PawnIoStatusEvaluator.Evaluate(version, detectionFailed: true);

        Assert.Equal(PawnIoState.Unknown, status.State);
        Assert.Null(status.Version);
    }

    [Fact]
    public void Version_IsTrimmedBeforeParsing()
    {
        var status = PawnIoStatusEvaluator.Evaluate("  2.2.0 ", detectionFailed: false);

        Assert.Equal(PawnIoState.Current, status.State);
        Assert.Equal("2.2.0", status.Version);
    }

    [Fact]
    public void Constants_MatchTheDocumentedPolicy()
    {
        Assert.Equal(new Version(2, 1, 0), PawnIoStatusEvaluator.MinimumRecommendedVersion);
        Assert.Equal("2.2.0", PawnIoStatusEvaluator.LatestKnownVersion);
    }
}
