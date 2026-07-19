using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end tests for the v2 C# emitter. Compiles Mist source through the
/// ProgramModel, emits C# via <see cref="CSharpEmitterV2"/>, compiles with
/// Roslyn in-process, runs the output DLL, and asserts on stdout.
/// </summary>
public sealed class EmitterV2IntegrationTests : IDisposable
{
    private readonly string outputDir;
    private readonly string binDir;

    public EmitterV2IntegrationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistEmitV2_" + Guid.NewGuid().ToString("N"));
        binDir = Path.Combine(outputDir, "bin");
        _ = Directory.CreateDirectory(outputDir);
        _ = Directory.CreateDirectory(binDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SimpliestSample_EmitsAndRunsCorrectly()
    {
        var source = """
            using Standard;
            namespace Simpliest;
            public class Program
            {
                public static void Main(string[] args)
                {
                    RunDemo();
                }
                public static void RunDemo()
                {
                    Console.WriteLine("General KENOBI!");
                }
            }
            """;

        var result = CompileAndRun(source, "Simpliest");

        Assert.True(result.CompileSucceeded, $"Compilation failed:\n{result.CompileDiagnostics}");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("General KENOBI!", result.Stdout.Trim());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Harness
    // ═══════════════════════════════════════════════════════════════════════

    private record RunResult(
        bool CompileSucceeded,
        string CompileDiagnostics,
        int ExitCode,
        string Stdout,
        string Stderr);

    private RunResult CompileAndRun(string source, string projectName)
    {
        // Stage 1-3: Lex & Parse
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("Program.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        // Stage 4: Build ProgramModel
        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("Program.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: binDir,
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var programModel = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            options,
            buildDiagnostics,
            new StandardTypesAmbientProvider());

        Assert.NotNull(programModel);

        // Stage 6: Emit via v2 emitter
        var emitDiagnostics = new DiagnosticBag();
        var emitter = new CSharpEmitterV2(programModel!, options, emitDiagnostics);
        var compilerResult = emitter.Emit();

        if(!compilerResult.Succeeded)
        {
            var diagText = string.Join("\n", compilerResult.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return new RunResult(false, diagText, -1, "", "");
        }

        // Run the compiled output
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = compilerResult.PrimaryOutputArtifactPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(30_000), "Process timed out after 30 seconds");

        return new RunResult(true, "", process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Ambient provider that registers Standard Library type shells with a Console.WriteLine method.
    /// </summary>
    private sealed class StandardTypesAmbientProvider : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            AmbientProviderTestHelper.RegisterStandardTypes(programModel);

            // Register Console with a static WriteLine(string) method.
            var consoleScope = new Scope(null);
            var consoleType = new ClassType("Console", "Standard", consoleScope, null, null!);
            _ = programModel.RegisterType(consoleType);

            var voidType = programModel.TypesByFQN["Standard.Void"];
            var stringType = programModel.TypesByFQN["Standard.String"];
            var writeLineMethod = new Method(
                "WriteLine",
                voidType,
                [new Parameter("value", stringType)],
                new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public });
            consoleType.MethodsList.Add(writeLineMethod);
            consoleType.Scope.AddMethod(writeLineMethod);

            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is MistType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Origin = TypeOrigin.Ambient;
                }
            }
        }
    }
}
