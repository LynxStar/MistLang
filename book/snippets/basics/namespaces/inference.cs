using System;

namespace Namespaces;

public class AppConfig
{
    private string appName;
    private string version;

    public AppConfig(string appName, string version)
    {
        this.appName = appName;
        this.version = version;
    }

    public string Display()
    {
        return $"App: {appName} v{version}";
    }
}
