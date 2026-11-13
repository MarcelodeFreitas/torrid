namespace Torrid.App.Startup;

public interface IStartupRegistration
{
    bool IsEnabled();

    /// <summary>Returns null on success, otherwise a short message fit to show the user. Never throws.</summary>
    string? SetEnabled(bool enable);
}
