using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class LocaleStartup
{
    private const string RuntimeResource = "Mist.Runtime.PosixLocale.cs";

    public static string? Source(CompilerOptions options)
    {
        if(options.OutputType != ProjectOutputType.Executable || options.EffectivePlatform != TargetPlatform.Windows)
        {
            return null;
        }

        var writer = new CSharpWriter();

        // MSBuild reads a .g.cs with nullable annotations off; the runtime is written for the context Mist compiles it in.
        writer.WriteLine("#nullable enable");
        writer.WriteLine("using System;");
        writer.Append(RuntimeSource().TrimEnd());
        writer.AppendLine();
        writer.WriteBlankLine();
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistLocaleStartup");

        using(writer.Block())
        {
            writer.WriteLine("[System.Runtime.CompilerServices.ModuleInitializer]");
            writer.WriteLine("internal static void Initialize()");

            using(writer.Block())
            {
                writer.WriteLine("PosixLocale.Apply();");
            }
        }

        return writer.ToString();
    }

    public static void EmitFile(CompilerOptions options, string outputDir, List<string> emittedFiles)
    {
        if(Source(options) is not { } source)
        {
            return;
        }

        var path = Path.Combine(outputDir, "__Locale.g.cs");
        File.WriteAllText(path, source);
        emittedFiles.Add(path);
    }

    private static string RuntimeSource()
    {
        using var stream = typeof(LocaleStartup).Assembly.GetManifestResourceStream(RuntimeResource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}