using Mist.Common.Diagnostics;

namespace Mist.Driver.Generators;

internal sealed record GeneratorServices
{
    public Func<DotnetInvocation, DotnetOutcome> Run { get; init; } = DotnetProcess.Run;

    public Func<GeneratorRequest, string, DiagnosticBag, string?> Compile { get; init; } = GeneratorCompiler.Build;

    public Func<string, DiagnosticBag, string?> Toolchain { get; init; } = GeneratorToolchain.Identity;
}