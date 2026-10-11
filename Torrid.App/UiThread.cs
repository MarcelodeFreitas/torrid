namespace Torrid.App;

using System.Windows;

public static class UiThread
{
    /// <summary>
    /// Queues <paramref name="action"/> on the WPF dispatcher without blocking the caller (never Invoke: a blocking
    /// call from the sensor timer thread can deadlock against a UI thread that is stopping that timer).
    /// Runs inline when there is no <see cref="Application"/> (unit tests).
    /// </summary>
    public static void Post(Action action)
    {
        if (Application.Current is { } app)
        {
            app.Dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
