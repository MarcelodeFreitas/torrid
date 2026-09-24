using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Notifications;
using Torrid.Core.Time;

namespace Torrid.Tests;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public sealed class RecordingToastNotifier : IToastNotifier
{
    public List<SensorReading> ShownAlerts { get; } = new();

    public void ShowRedAlert(SensorReading reading) => ShownAlerts.Add(reading);
}

public class NotificationServiceTests
{
    private static readonly Dictionary<SensorKind, ThresholdBand> Thresholds = new()
    {
        [SensorKind.CpuPackage] = new ThresholdBand { BlueMax = 40, GreenMax = 70, YellowMax = 85 },
    };

    private static SensorReading Cpu(double value) => new("cpu", "CPU", SensorKind.CpuPackage, null, value);

    [Fact]
    public void Evaluate_WhenCrossingIntoRed_FiresExactlyOneAlert()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        service.Evaluate(new[] { Cpu(90) }, Thresholds);

        Assert.Single(notifier.ShownAlerts);
    }

    [Fact]
    public void Evaluate_WhenSustainedInRedAcrossManyPolls_DoesNotSpamEveryTick()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        for (var i = 0; i < 10; i++)
        {
            service.Evaluate(new[] { Cpu(90 + i * 0.1) }, Thresholds);
        }

        Assert.Single(notifier.ShownAlerts);
    }

    [Fact]
    public void Evaluate_WhenFlappingRightAtBoundary_DoesNotRefireUntilRealDropBelowRed()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        service.Evaluate(new[] { Cpu(85.1) }, Thresholds); // enters red -> 1 alert
        service.Evaluate(new[] { Cpu(84.9) }, Thresholds); // yellow, not red -> resets hysteresis
        service.Evaluate(new[] { Cpu(85.1) }, Thresholds); // re-enters red -> 2nd alert

        Assert.Equal(2, notifier.ShownAlerts.Count);
    }

    [Fact]
    public void Evaluate_WhenSustainedPastRefireInterval_FiresAgain()
    {
        var clock = new FakeClock();
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, clock, refireMinutes: 10);

        service.Evaluate(new[] { Cpu(90) }, Thresholds); // 1st alert
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        service.Evaluate(new[] { Cpu(90) }, Thresholds); // still sustained, too soon -> no alert
        clock.UtcNow = clock.UtcNow.AddMinutes(6);
        service.Evaluate(new[] { Cpu(90) }, Thresholds); // 11 min since 1st alert -> 2nd alert

        Assert.Equal(2, notifier.ShownAlerts.Count);
    }

    [Fact]
    public void Evaluate_WithNullValue_DoesNotThrow()
    {
        var notifier = new RecordingToastNotifier();
        var service = new NotificationService(notifier, new FakeClock(), refireMinutes: 0);

        var exception = Record.Exception(() =>
            service.Evaluate(new[] { new SensorReading("cpu", "CPU", SensorKind.CpuPackage, null, null) }, Thresholds));

        Assert.Null(exception);
        Assert.Empty(notifier.ShownAlerts);
    }
}
