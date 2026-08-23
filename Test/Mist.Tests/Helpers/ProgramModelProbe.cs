// Tier 3 (Component) harness: builds a real ProgramModel over minimal source and hands back the bound
// expression a test is about. Real ambient shells, real scope chain, real interner; no Roslyn and no
// process spawning.
//
// WHY TESTS BUILT ON THIS ASSERT ON THE MODEL RATHER THAN ON OUTPUT. Emission re-writes a construct the
// way source spelled it and lets Roslyn resolve it again -- a call, and equally a cast, since C# will
// not let a conversion operator be called by name. So a program that prints the right answer is equally
// consistent with Mist having chosen nothing, and only the model can distinguish the two.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

/// <summary>
/// Wraps a statement under test in a probe method, builds the program model, and returns what was
/// bound. A deriving class supplies the types the statement draws on and the locals declared ahead of
/// it, so each test states only the construct it is about.
/// </summary>
public abstract class ProgramModelProbe : IDisposable
{
    private readonly string outputDir;

    protected ProgramModelProbe(string tempPrefix)
    {
        outputDir = Path.Combine(Path.GetTempPath(), tempPrefix + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    /// <summary>
    /// Types declared alongside the probe class, available to every statement.
    /// </summary>
    protected abstract string Fixtures { get; }

    /// <summary>
    /// Statements run before the one under test, supplying the typed locals it draws on.
    /// </summary>
    protected abstract string Preamble { get; }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if(disposing && Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* a leaked temp dir is not worth failing a test over */ }
        }
    }

    private protected static IEnumerable<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value == id);
    }

    /// <summary>
    /// The initializer of the statement under test, asserted to be the expected bound kind.
    /// </summary>
    private protected T InitializerOf<T>(string statement, string extraTypes = "", string extraPreamble = "")
        where T : Expression
    {
        var initializer = DeclarationIn(statement, extraTypes, extraPreamble).Initializer;
        Assert.NotNull(initializer);
        return Assert.IsType<T>(initializer);
    }

    private protected LocalVariable SingleLocal(string statement, string extraTypes = "", string extraPreamble = "")
    {
        return DeclarationIn(statement, extraTypes, extraPreamble).Local;
    }

    private protected ImmutableArray<Diagnostic> BuildRun(
        string statement, string extraTypes = "", string extraPreamble = "")
    {
        _ = BuildModel(statement, extraTypes, extraPreamble, out var diagnostics);
        return diagnostics;
    }

    /// <summary>
    /// The LAST local declaration in the probe method, since the preamble declares several before the
    /// one the test supplied.
    /// </summary>
    private protected LocalDeclarationStatement DeclarationIn(
        string statement, string extraTypes, string extraPreamble = "")
    {
        var model = BuildModel(statement, extraTypes, extraPreamble, out _);

        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var run = probe.MethodsList.Single(m => m.Name == "Run");

        Assert.NotNull(run.Body);
        return run.Body.Statements.OfType<LocalDeclarationStatement>().Last();
    }

    private protected ProgramModelClass BuildModel(
        string statement, string extraTypes, string extraPreamble, out ImmutableArray<Diagnostic> diagnostics)
    {
        var source = $$"""
            namespace Probe;

            {{Fixtures}}

            {{extraTypes}}

            public class Probe
            {
                public static void Run()
                {
                    {{Preamble}}
                    {{extraPreamble}}
                    {{statement}}
                }
            }
            """;

        return Build(source, out diagnostics);
    }

    /// <summary>
    /// Lexes, parses and builds the program model, returning the BUILD diagnostics. Parsing is
    /// asserted clean separately so a malformed fixture fails as itself rather than as a missing or
    /// unexpected diagnostic.
    /// </summary>
    private protected ProgramModelClass Build(string source, out ImmutableArray<Diagnostic> diagnostics)
    {
        var fileName = Path.Combine(outputDir, "Program.mist");
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText(fileName, source);
        var tokens = new MistLexer().Lex(sourceText, parseDiagnostics);
        var parseResult = new MistParser().Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);
        Assert.Empty(parseDiagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(fileName),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var programModel = ProgramModelBuilder.Build(
            new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText))),
            ImmutableArray.Create(parseResult.Root),
            options,
            buildDiagnostics,
            new StandardTypesAmbientProvider());

        diagnostics = buildDiagnostics.ToImmutableArray();
        return Assert.IsType<ProgramModelClass>(programModel);
    }

    /// <summary>
    /// Registers the Standard type shells only, so primitive aliases resolve without loading any CLR
    /// assembly.
    /// </summary>
    private sealed class StandardTypesAmbientProvider : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            AmbientProviderTestHelper.RegisterStandardTypes(programModel);

            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is DeclaredType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Assembly = MistAssembly.Ambient;
                }
            }

            DeclareStringLength(programModel);
        }

        /// <summary>
        /// The shells carry no members, and this one is needed to state anything about a PRIMITIVE
        /// scrutinee: <c>s is { Length: &gt; 3 }</c> resolves only because Standard.String really does
        /// declare Length (Bootstrap/String.mist), so a fixture without it would make the claim
        /// untestable rather than false.
        /// </summary>
        private static void DeclareStringLength(ProgramModelClass programModel)
        {
            if(programModel.TypesByFQN[WellKnownTypes.String.FullyQualifiedName] is not ClassType stringType)
            {
                return;
            }

            stringType.PropertiesList.Add(new Property(
                "Length",
                programModel.TypesByFQN[WellKnownTypes.Int32.FullyQualifiedName],
                new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true })
            {
                Getter = new PropertyAccessor(),
            });
        }
    }
}