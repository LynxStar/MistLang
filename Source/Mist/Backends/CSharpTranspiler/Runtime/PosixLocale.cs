using System.Globalization;

namespace Mist.Backends.CSharpTranspiler.Runtime;

// This file is embedded verbatim into every program compiled for Windows, after a `using System;` that
// Lowering.LocaleStartup supplies, so it may name nothing outside System and System.Globalization.
internal static class PosixLocale
{
    private static readonly string[] Variables = ["LC_ALL", "LC_MESSAGES", "LANG"];

    public static void Apply()
    {
        if(Culture(Environment.GetEnvironmentVariable) is not { } culture)
        {
            return;
        }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static CultureInfo? Culture(Func<string, string?> environment)
    {
        foreach(var variable in Variables)
        {
            var value = environment(variable);
            if(!string.IsNullOrEmpty(value))
            {
                return Named(value);
            }
        }

        return null;
    }

    private static CultureInfo? Named(string value)
    {
        var name = value.Split('.', '@')[0];
        if(name is "C" or "POSIX")
        {
            return CultureInfo.InvariantCulture;
        }

        if(name.Length == 0)
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name.Replace('_', '-'), predefinedOnly: true);
        }
        catch(ArgumentException)
        {
            return null;
        }
    }
}