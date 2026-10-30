using System.Runtime.InteropServices;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Driver.Generators;

internal static class GeneratorCompiler
{
    public static string? Build(GeneratorRequest request, string staging, DiagnosticBag diagnostics)
    {
        var configuration = request.Generator;
        var folder = Path.GetDirectoryName(request.Reference.ProjectPath)!;
        var options = new CompilerOptions(folder, SourcePathSelection.ForReferencedProject(folder, SourcePathSelection.EnumerateCandidates(folder)),
            BackendId.CSharp, Path.Combine(staging, "out"), Path.Combine(staging, "bin"), configuration.Name, configuration.NamespaceRoot,
            configuration.EntryPoint, configuration.GlobalUsings, configuration.Packages, configuration.FrameworkReferences,
            configuration.TargetFramework, CsprojProperties: configuration.CsprojProperties,
            OutputType: ProjectOutputType.Executable, Platform: TargetPlatforms.Host, RuntimeIdentifier: RuntimeInformation.RuntimeIdentifier)
        {
            Configuration = configuration,
            DiagnosticOperation = diagnostics.Operation
        };
        var result = new CompilerDriver(new MistLexer(), new MistParser()).Compile(options);
        foreach(var diagnostic in result.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        return result.Succeeded ? result.PrimaryOutputArtifactPath : null;
    }
}