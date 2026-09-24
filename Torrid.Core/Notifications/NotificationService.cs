namespace Torrid.Core.Notifications;

using Torrid.Core.Config;
using Torrid.Core.Models;
using Torrid.Core.Time;

public sealed class NotificationService
{
    private readonly IToastNotifier _notifier;
    private readonly IClock _clock;
    private readonly int _refireMinutes;
    private readonly Dictionary<string, DateTime> _lastNotifiedUtc = new();
    private readonly HashSet<string> _currentlyRed = new();

    public NotificationService(IToastNotifier notifier, IClock clock, int refireMinutes)
    {
        _notifier = notifier;
        _clock = clock;
        _refireMinutes = refireMinutes;
    }

    public void Evaluate(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<SensorKind, ThresholdBand> thresholds)
    {
        foreach (var reading in readings)
        {
            if (reading.ValueCelsius is not double value) continue;
            if (!thresholds.TryGetValue(reading.Kind, out var band)) continue;

            var isRed = ColorThresholdEvaluator.GetColor(value, band) == ColorZone.Red;
            var wasRed = _currentlyRed.Contains(reading.Id);

            if (isRed && !wasRed)
            {
                _notifier.ShowRedAlert(reading);
                _lastNotifiedUtc[reading.Id] = _clock.UtcNow;
                _currentlyRed.Add(reading.Id);
            }
            else if (isRed && wasRed && _refireMinutes > 0 &&
                     _clock.UtcNow - _lastNotifiedUtc[reading.Id] >= TimeSpan.FromMinutes(_refireMinutes))
            {
                _notifier.ShowRedAlert(reading);
                _lastNotifiedUtc[reading.Id] = _clock.UtcNow;
            }
            else if (!isRed && wasRed)
            {
                _currentlyRed.Remove(reading.Id);
            }
        }
    }
}
