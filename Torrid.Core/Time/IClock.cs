namespace Torrid.Core.Time;

public interface IClock
{
    DateTime UtcNow { get; }
}
