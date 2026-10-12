namespace Torrid.Core.Notifications;

using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Time;

public sealed class NotificationService
{
    private static readonly Dictionary<SensorKind, ThresholdBand> DefaultBands = AppConfig.DefaultThresholds();

    private readonly IToastNotifier _notifier;
    private readonly IClock _clock;
    private readonly double _reArmMarginCelsius;
    private readonly Dictionary<string, DateTime> _lastNotifiedUtc = new();
    private readonly HashSet<string> _currentlyRed = new();

    public NotificationService(IToastNotifier notifier, IClock clock, int refireMinutes, double reArmMarginCelsius = 3.0)
    {
        _notifier = notifier;
        _clock = clock;
        RefireMinutes = refireMinutes;
        _reArmMarginCelsius = reArmMarginCelsius;
    }

    public int RefireMinutes { get; set; }

    public void Evaluate(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)
    {
        foreach (var reading in readings)
        {
            if (reading.ValueCelsius is not double value) continue;
            if (!thresholds.TryGetValue(reading.Kind, out var band) && !DefaultBands.TryGetValue(reading.Kind, out band)) continue;

            var isRed = ColorThresholdEvaluator.GetColor(value, band) == ColorZone.Red;
            var wasRed = _currentlyRed.Contains(reading.Id);

            if (isRed && !wasRed)
            {
                _notifier.ShowRedAlert(reading);
                _lastNotifiedUtc[reading.Id] = _clock.UtcNow;
                _currentlyRed.Add(reading.Id);
            }
            else if (isRed && wasRed && RefireMinutes > 0 &&
                     _clock.UtcNow - _lastNotifiedUtc[reading.Id] >= TimeSpan.FromMinutes(RefireMinutes))
            {
                _notifier.ShowRedAlert(reading);
                _lastNotifiedUtc[reading.Id] = _clock.UtcNow;
            }
            else if (wasRed && value <= band.YellowMax - _reArmMarginCelsius)
            {
                _currentlyRed.Remove(reading.Id);
            }
        }
    }
}
