namespace Torrid.Core.Config;

public interface IConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
}
