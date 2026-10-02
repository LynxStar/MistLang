using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

// Whether a compiled program carries the startup hook that adopts the POSIX locale variables, decided
// from hand-built options. Only an executable compiled for Windows needs it: elsewhere .NET reads the
// variables itself, and a library must not change the culture of the process that loads it.
[Trait("Tier", "1")]
public sealed class LocaleStartupTests
{
    [Fact]
    public void AnExecutableForWindows_AdoptsTheLocaleBeforeAnyOfItsCodeRuns()
    {
        var source = LocaleStartup.Source(Options(TargetPlatform.Windows, ProjectOutputType.Executable));

        Assert.NotNull(source);
        Assert.StartsWith("using System;", source, StringComparison.Ordinal);
        Assert.Contains("internal static class PosixLocale", source, StringComparison.Ordinal);
        Assert.Contains(
            "[System.Runtime.CompilerServices.ModuleInitializer]\r\n    internal static void Initialize()\r\n    {\r\n        PosixLocale.Apply();",
            source.ReplaceLineEndings("\r\n"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TargetPlatform.Linux, ProjectOutputType.Executable)]
    [InlineData(TargetPlatform.MacOS, ProjectOutputType.Executable)]
    [InlineData(TargetPlatform.Windows, ProjectOutputType.Library)]
    public void AnythingElse_CarriesNoHook(TargetPlatform platform, ProjectOutputType outputType)
    {
        Assert.Null(LocaleStartup.Source(Options(platform, outputType)));
    }

    [Fact]
    public void NoPlatform_MeansTheMachineCompiling()
    {
        var source = LocaleStartup.Source(Options(null, ProjectOutputType.Executable));

        Assert.Equal(TargetPlatforms.Host == TargetPlatform.Windows, source is not null);
    }

    private static CompilerOptions Options(TargetPlatform? platform, ProjectOutputType outputType)
    {
        return new CompilerOptions(
            ProjectFolder: "project",
            SourcePaths: [],
            Backend: new BackendId("csharp"),
            OutputDirectory: "out",
            BinDirectory: "bin",
            ProjectName: "Program",
            NamespaceRoot: "Program",
            OutputType: outputType,
            Platform: platform);
    }
}