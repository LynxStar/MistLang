// Tier 3 (Component): `unknown` is refused as the declared type of writable storage, asserted
// against the diagnostics of a real ProgramModelBuilder run over minimal source. Real ambient
// shells, real scope chain, real interner; no Roslyn and no process spawning, because the rule is a
// declaration-site fact and crosses neither of those seams.
//
// These are NEGATIVE invariants -- what the compiler must refuse -- which is why they need tests at
// all. The Tier 6 orchestrator can only demonstrate programs that compile, so it is structurally
// incapable of showing that a rejected program is rejected.
//
// SCOPE. This covers the storage ban only. The rest of what makes `unknown` a top type -- that
// every non-void type widens into it, and that nothing comes back out without a pattern -- is type
// checking, and belongs to the analysis stage, which does not exist yet. So a program that assigns
// an `unknown` to an int is NOT rejected here, and there is deliberately no test claiming it is.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Integration;

[Trait("Tier", "3")]
public sealed class UnknownStorageBanTests : IDisposable
{
    private const string StorageId = "MIST0394";

    private readonly string outputDir;

    public UnknownStorageBanTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistUnknownStorage_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("public unknown slot;", "Field 'Holder.slot'")]
    [InlineData("public unknown Prop { get; set; }", "Property 'Holder.Prop'")]
    [InlineData("public unknown Ratio => 1;", "Property 'Holder.Ratio'")]
    [InlineData("public unknown this[int i] { get { return 1; } }", "Indexer 'Holder.this[]'")]
    public void UnknownAtAStoragePosition_IsRefused(string member, string expectedSubject)
    {
        var diagnostics = BuildHolder(member);

        var reported = Assert.Single(Errors(diagnostics, StorageId));
        Assert.Contains(expectedSubject, reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A getter-only property is refused on the same rule as a settable one. The ban is on the
    /// declared type, not on the presence of a setter -- the alternative would make the rule
    /// depend on which accessors happen to be written today.
    /// </summary>
    [Fact]
    public void UnknownOnAGetterOnlyProperty_IsStillRefused()
    {
        var diagnostics = BuildHolder("public unknown Value { get { return 1; } }");

        _ = Assert.Single(Errors(diagnostics, StorageId));
    }

    /// <summary>
    /// The ban follows the type expression rather than stopping at its head, so a generic argument,
    /// a union constituent and a tuple element are each reached.
    /// <para>
    /// The expected spelling is asserted along with the rejection, because a diagnostic naming a
    /// type the user cannot find in their file is most of the way to being useless. A generic
    /// definition is registered under an arity-bearing name, so the type reported here is exactly
    /// where <c>Box&lt;&gt;&lt;unknown&gt;</c> surfaces if that marker is not stripped.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("public Box<unknown> boxed;", "'Box<unknown>'")]
    [InlineData("public Box<Box<unknown>> nested;", "'Box<Box<unknown>>'")]
    [InlineData("public int | unknown either;", "'int | unknown'")]
    [InlineData("public (int, unknown) pair;", "'(int, unknown)'")]
    // An array is REPRESENTED as Standard.Array<T, Length> but SPELLED with a rank suffix, and its
    // second argument is a length rather than a type, so a general rendering of the representation
    // reports `int[]` as `Array<int, int>`.
    [InlineData("public (int[], unknown) unsized;", "'(int[], unknown)'")]
    [InlineData("public (int[5], unknown) sized;", "'(int[5], unknown)'")]
    public void UnknownReachedThroughATypeExpression_IsRefused(string member, string expectedType)
    {
        var diagnostics = BuildHolder(member, extraTypes: "public class Box<T> { }");

        var reported = Assert.Single(Errors(diagnostics, StorageId));
        Assert.Contains(expectedType, reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The counter-example that matters most. An unsized array carries an unknown LENGTH, which is
    /// a dependent value of known domain and not the top type. Confusing the two would ban every
    /// array-typed field in the language, so this pins the distinction rather than the rule.
    /// </summary>
    [Theory]
    [InlineData("public int concrete;")]
    [InlineData("public int[] numbers;")]
    [InlineData("public string[] names;")]
    [InlineData("public Box<int> boxed;")]
    [InlineData("public (int, string) pair;")]
    public void AStoragePositionWithoutUnknown_IsAccepted(string member)
    {
        var diagnostics = BuildHolder(member, extraTypes: "public class Box<T> { }");

        Assert.Empty(Errors(diagnostics, StorageId));
    }

    /// <summary>
    /// A field typed by its enclosing generic's own parameter is not a banned position: <c>T</c> is
    /// only <c>unknown</c> once something binds it to <c>unknown</c>, and nothing at this
    /// declaration has.
    /// </summary>
    [Fact]
    public void AFieldTypedByAnOpenTypeParameter_IsAccepted()
    {
        var diagnostics = Build("""
            namespace Probe;

            public class Box<T>
            {
                public T slot;
            }
            """);

        Assert.Empty(Errors(diagnostics, StorageId));
    }

    /// <summary>
    /// The ban is on storage, so the positions that merely carry a value through are left alone. A
    /// parameter and a return type are how an <c>unknown</c> is meant to be accepted and handed
    /// back, and banning them would leave the type with no usable position at all.
    /// </summary>
    [Fact]
    public void UnknownAsAParameterOrReturnType_IsAccepted()
    {
        var diagnostics = Build("""
            namespace Probe;

            public class Probe
            {
                public static unknown Passthrough(unknown value)
                {
                    return value;
                }
            }
            """);

        Assert.Empty(Errors(diagnostics, StorageId));
    }

    /// <summary>
    /// An <c>unknown</c> local is not storage in the sense the ban means: it is a binding, not a
    /// slot another declaration can read through, so widening into one stays legal.
    /// </summary>
    [Fact]
    public void UnknownAsALocal_IsAccepted()
    {
        var diagnostics = Build("""
            namespace Probe;

            public class Probe
            {
                public static void Run()
                {
                    unknown slot = 42;
                }
            }
            """);

        Assert.Empty(Errors(diagnostics, StorageId));
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static IEnumerable<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Number.Value == id);
    }

    private ImmutableArray<Diagnostic> BuildHolder(string member, string extraTypes = "")
    {
        return Build($$"""
            namespace Probe;

            {{extraTypes}}

            public class Holder
            {
                {{member}}
            }
            """);
    }

    /// <summary>
    /// Lexes, parses and builds the program model, returning only the BUILD diagnostics. Parsing is
    /// asserted clean separately so that a malformed test fixture fails as itself rather than as an
    /// absent diagnostic.
    /// </summary>
    private ImmutableArray<Diagnostic> Build(string source)
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
            OutputType: ProjectOutputType.Library);

        var buildDiagnostics = new DiagnosticBag();
        var programModel = ProgramModelBuilder.Build(
            new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText, "Probe", outputDir) { Ast = parseResult.Root }), OutputType: ProjectOutputType.Library),
            options,
            buildDiagnostics,
            new StandardTypesAmbientProvider());

        Assert.NotNull(programModel);
        return buildDiagnostics.ToImmutableArray();
    }

    /// <summary>
    /// Registers the Standard type shells only, so primitive aliases resolve without loading any
    /// CLR assembly.
    /// </summary>
    private sealed class StandardTypesAmbientProvider : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            AmbientProviderTestHelper.RegisterStandardTypes(programModel);

            // Attribute the Standard shells to the ambient assembly so the Phase 2a source-type
            // build loop skips them: they carry no AST declaration to resolve.
            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is DeclaredType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Assembly = MistAssembly.Ambient;
                }
            }
        }
    }
}