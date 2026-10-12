using Torrid.App.Drivers;
using Torrid.Core.Drivers;
using Xunit.Abstractions;

namespace Torrid.Tests;

public class RegistryPawnIoDetectorTests
{
    private readonly ITestOutputHelper _output;

    public RegistryPawnIoDetectorTests(ITestOutputHelper output) => _output = output;

    // Reads the real registry of whatever machine runs the tests, so it only checks that the result is self-consistent.
    [Fact]
    public void Detect_ReadsTheRealRegistryWithoutThrowing_AndReturnsAConsistentStatus()
    {
        var status = new RegistryPawnIoDetector().Detect();

        _output.WriteLine($"PawnIO on this machine: {status.State} {status.Version}");
        Assert.True(Enum.IsDefined(status.State));
        if (status.State is PawnIoState.Current or PawnIoState.Outdated)
        {
            Assert.False(string.IsNullOrWhiteSpace(status.Version));
        }

        if (status.State == PawnIoState.NotInstalled)
        {
            Assert.Null(status.Version);
        }
    }
}
