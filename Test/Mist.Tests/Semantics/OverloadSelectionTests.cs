// Tier 3 (Component): which overload a call selects, asserted against a real ProgramModelBuilder run
// over minimal source. Real ambient shells, real scope chain, real interner; no Roslyn and no process
// spawning.
//
// WHY THESE ASSERT ON THE MODEL RATHER THAN ON OUTPUT. Emission re-writes a call the way source
// spelled it and lets Roslyn resolve it again, so a program that prints the right answer is equally
// consistent with Mist having chosen nothing.
//
// Several fixtures below declare the wanted overload SECOND on purpose. Taking the first candidate is
// what selection replaced, so a fixture that happens to want the first one passes either way.

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
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

public sealed class OverloadSelectionTests : IDisposable
{
    private const string NoOverloadId = "MIST_PM_NO_APPLICABLE_OVERLOAD";
    private const string AmbiguousId = "MIST_PM_AMBIGUOUS_OVERLOAD";
    private const string UnresolvableId = "MIST_PM_UNRESOLVABLE_OVERLOAD";
    private const string MethodGroupId = "MIST_PM_METHOD_GROUP_NOT_CONVERTED";

    private const string Mixer = """
        public class Mixer
        {
            public static int Take(int a, int b) { return 1; }

            public static int Take(double a, double b) { return 2; }
        }
        """;

    private const string Fixtures = """
        public class Printer
        {
            public Printer() { }

            public Printer(int width) { }

            public string Format(int value) { return "int"; }

            public string Format(bool value) { return "bool"; }

            public string Format(double value) { return "double"; }

            public string Format(string value) { return "string"; }
        }

        public class Sizer
        {
            public static int Of(int a) { return 1; }

            public static int Of(int a, int b) { return 2; }

            public static int Of(int a, int b, int c) { return 3; }
        }

        public class Plain
        {
            public static int Once(int a) { return a; }
        }
        """;

    private readonly string outputDir;

    public OverloadSelectionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistOverload_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* a leaked temp dir is not worth failing a test over */ }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Arguments are explicitly typed locals rather than literals, so a wrong answer here cannot be
    /// blamed on how <c>1.5</c> types.
    /// </summary>
    [Theory]
    [InlineData("i", "Standard.Int32")]
    [InlineData("b", "Standard.Boolean")]
    [InlineData("d", "Standard.Double")]
    [InlineData("s", "Standard.String")]
    public void SameArityOverloads_SelectByArgumentType(string argument, string expectedParameterType)
    {
        var invocation = SingleInvocation($"var chosen = p.Format({argument});");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal(expectedParameterType, parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void ALiteralArgument_SelectsTheMatchingOverload()
    {
        var invocation = SingleInvocation("var chosen = p.Format(\"text\");");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.String", parameter.Type.FullyQualifiedName);
    }

    /// <summary>
    /// The string overload is declared FIRST deliberately: a fixture whose wanted overload sits first
    /// passes even when nothing is selected at all.
    /// </summary>
    [Fact]
    public void TheSelectedOverloadsReturnType_FlowsIntoAnInferredLocal()
    {
        var local = SingleLocal(
            "var picked = Widen.Pick(i);",
            extraTypes: """
                public class Widen
                {
                    public static string Pick(string value) { return value; }

                    public static int Pick(int value) { return value; }
                }
                """);

        Assert.Equal("Standard.Int32", local.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("i", 1)]
    [InlineData("i, i", 2)]
    [InlineData("i, i, i", 3)]
    public void OverloadsDifferingInArity_SelectByArgumentCount(string arguments, int expectedArity)
    {
        var invocation = SingleInvocation($"var chosen = Sizer.Of({arguments});");

        Assert.NotNull(invocation.Method);
        Assert.Equal(expectedArity, invocation.Method.ParametersList.Count);
    }

    /// <summary>
    /// Constructors reach the same selection because <c>Constructor</c> derives from <c>Method</c>.
    /// </summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData("i", 1)]
    public void ConstructorOverloads_SelectByArgumentCount(string arguments, int expectedArity)
    {
        var creation = SingleNew($"var made = new Printer({arguments});");

        Assert.NotNull(creation.Constructor);
        Assert.Equal(expectedArity, creation.Constructor.ParametersList.Count);
    }

    [Fact]
    public void ACallMatchingNoArity_IsRefusedAtTheMistSource()
    {
        var diagnostics = BuildRun("var chosen = Sizer.Of(i, i, i, i);");

        var reported = Assert.Single(Errors(diagnostics, NoOverloadId));
        Assert.Contains("Of", reported.Message, StringComparison.Ordinal);
        Assert.Contains("1, 2, 3", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Named as a constructor, so the reader is not sent looking for a method.
    /// </summary>
    [Fact]
    public void AConstructionMatchingNoArity_IsRefused()
    {
        var diagnostics = BuildRun("var made = new Printer(i, i, i);");

        var reported = Assert.Single(Errors(diagnostics, NoOverloadId));
        Assert.Contains("constructor 'Printer'", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A resolver that simply rejects more is not a resolver that chooses better, so every rule above
    /// needs a case it must leave alone.
    /// </summary>
    [Theory]
    [InlineData("var chosen = Sizer.Of(i);")]
    [InlineData("var chosen = Sizer.Of(i, i);")]
    [InlineData("var chosen = Sizer.Of(i, i, i);")]
    [InlineData("var chosen = p.Format(i);")]
    [InlineData("var chosen = Plain.Once(i);")]
    [InlineData("var made = new Printer();")]
    [InlineData("var made = new Printer(i);")]
    public void AWellFormedCall_IsNotRefused(string statement)
    {
        var diagnostics = BuildRun(statement);

        Assert.Empty(Errors(diagnostics, NoOverloadId));
        Assert.Empty(Errors(diagnostics, AmbiguousId));
        Assert.Empty(Errors(diagnostics, UnresolvableId));
    }

    [Fact]
    public void ANonOverloadedCall_StillRecordsItsMethod()
    {
        var invocation = SingleInvocation("var chosen = Plain.Once(i);");

        Assert.NotNull(invocation.Method);
        Assert.Equal("Once", invocation.Method.Name);
    }

    [Fact]
    public void ATypeWithNoDeclaredConstructor_RecordsNoneAndIsNotRefused()
    {
        var diagnostics = BuildRun("var made = new Bare();", extraTypes: "public class Bare { }");
        Assert.Empty(Errors(diagnostics, NoOverloadId));

        var creation = SingleNew("var made = new Bare();", extraTypes: "public class Bare { }");
        Assert.Null(creation.Constructor);
    }

    /// <summary>
    /// Settled by APPLICABILITY alone, with no tiebreak involved: only the double form accepts both
    /// arguments, so selecting the int form would pick a candidate the call cannot legally reach.
    /// </summary>
    [Fact]
    public void MixedNumericArguments_SelectTheOnlyApplicableOverload()
    {
        var invocation = SingleInvocation("var chosen = Mixer.Take(i, d);", extraTypes: Mixer);

        Assert.NotNull(invocation.Method);
        Assert.All(invocation.Method.ParametersList, p => Assert.Equal("Standard.Double", p.Type.FullyQualifiedName));
    }

    /// <summary>
    /// Both <c>Take</c> forms accept two ints, so applicability leaves a contest and betterness
    /// settles it.
    /// </summary>
    [Fact]
    public void AnExactMatch_BeatsAWidening()
    {
        var invocation = SingleInvocation("var chosen = Mixer.Take(i, i);", extraTypes: Mixer);

        Assert.NotNull(invocation.Method);
        Assert.All(invocation.Method.ParametersList, p => Assert.Equal("Standard.Int32", p.Type.FullyQualifiedName));
    }

    /// <summary>
    /// Neither candidate is exact, so betterness falls to the narrower target. The double form is
    /// declared first, so first-match would pick it.
    /// </summary>
    [Fact]
    public void WithNoExactMatch_TheNarrowerTargetWins()
    {
        var invocation = SingleInvocation(
            "var chosen = Widths.Hold(i);",
            extraTypes: """
                public class Widths
                {
                    public static int Hold(double value) { return 1; }

                    public static int Hold(long value) { return 2; }
                }
                """);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Standard.Int64", parameter.Type.FullyQualifiedName);
    }

    /// <summary>
    /// Applicability is not only numeric: <c>Accept</c> is declared with the string form second, and
    /// only the base-typed one admits a <c>Cub</c>.
    /// </summary>
    [Fact]
    public void ADerivedArgument_ReachesABaseTypedParameter()
    {
        var invocation = SingleInvocation(
            "var chosen = Sink.Accept(new Cub());",
            extraTypes: """
                public class Animal { }

                public class Cub : Animal { }

                public class Sink
                {
                    public static int Accept(Animal a) { return 1; }

                    public static int Accept(string s) { return 2; }
                }
                """);

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Probe.Animal", parameter.Type.FullyQualifiedName);
    }

    /// <summary>
    /// Both forms accept two ints and each is exact in one position and widened in the other, so
    /// neither beats the other.
    /// </summary>
    [Fact]
    public void AGenuineTie_IsReportedAsAmbiguous()
    {
        var diagnostics = BuildRun(
            "var chosen = Cross.Pair(i, i);",
            extraTypes: """
                public class Cross
                {
                    public static int Pair(int a, double b) { return 1; }

                    public static int Pair(double a, int b) { return 2; }
                }
                """);

        var reported = Assert.Single(Errors(diagnostics, AmbiguousId));
        Assert.Contains("Pair", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(int, double)", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(double, int)", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tie names only the candidates nothing beats. An int reaches decimal, float and double, but
    /// float beats double, so double must not appear -- comparing each candidate against a running
    /// winner rather than against every other would leave it there.
    /// </summary>
    [Fact]
    public void AReportedTie_ExcludesCandidatesAnotherBeats()
    {
        var diagnostics = BuildRun(
            "var chosen = Spread.Hold(i);",
            extraTypes: """
                public class Spread
                {
                    public static int Hold(decimal value) { return 1; }

                    public static int Hold(double value) { return 2; }

                    public static int Hold(float value) { return 3; }
                }
                """);

        var reported = Assert.Single(Errors(diagnostics, AmbiguousId));
        Assert.Contains("decimal", reported.Message, StringComparison.Ordinal);
        Assert.Contains("float", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("double", reported.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An int reaches neither a string nor a bool by any conversion ConversionRelation models, so
    /// applicability eliminates every candidate and leaves nothing to choose on. Refused rather than
    /// bound to whichever form is declared first, which is the one thing a partial relation does not
    /// license: declining to refuse is not the same as inventing a selection.
    /// </summary>
    [Fact]
    public void WhenNoCandidateIsKnownApplicable_TheCallIsRefused()
    {
        const string unreachable = """
            public class Unreachable
            {
                public static int Take(string a) { return 1; }

                public static int Take(bool a) { return 2; }
            }
            """;

        var diagnostics = BuildRun("var chosen = Unreachable.Take(i);", extraTypes: unreachable);

        var reported = Assert.Single(Errors(diagnostics, UnresolvableId));
        Assert.Contains("Take", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(string)", reported.Message, StringComparison.Ordinal);
        Assert.Contains("(bool)", reported.Message, StringComparison.Ordinal);

        Assert.Empty(Errors(diagnostics, NoOverloadId));
        Assert.Empty(Errors(diagnostics, AmbiguousId));
    }

    /// <summary>
    /// <c>BackendDiagnostics.MethodGroupReachedEmission</c> is meant to be unreachable, which holds
    /// only while every position unable to consume a group reports one instead. These are the two
    /// such positions: a name nothing follows, and an argument, which cannot resolve because the
    /// overload it belongs to is not selected until after its arguments are bound.
    /// </summary>
    [Theory]
    [InlineData("var f = Plain.Once;")]
    [InlineData("var c = Plain.Once(Plain.Once);")]
    public void AMethodGroupNothingConsumes_IsReportedAtBinding(string statement)
    {
        var diagnostics = BuildRun(statement);

        var reported = Assert.Single(Errors(diagnostics, MethodGroupId));
        Assert.Contains("Once", reported.Message, StringComparison.Ordinal);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static IEnumerable<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value == id);
    }

    private InvocationExpression SingleInvocation(string statement, string extraTypes = "")
    {
        return Assert.IsType<InvocationExpression>(InitializerOf(statement, extraTypes));
    }

    private NewExpression SingleNew(string statement, string extraTypes = "")
    {
        return Assert.IsType<NewExpression>(InitializerOf(statement, extraTypes));
    }

    private LocalVariable SingleLocal(string statement, string extraTypes = "")
    {
        return DeclarationIn(statement, extraTypes).Local;
    }

    private Expression InitializerOf(string statement, string extraTypes)
    {
        var initializer = DeclarationIn(statement, extraTypes).Initializer;
        Assert.NotNull(initializer);
        return initializer;
    }

    /// <summary>
    /// The LAST local declaration in <c>Run</c>, since the preamble declares several before the one
    /// the test supplied.
    /// </summary>
    private LocalDeclarationStatement DeclarationIn(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, out _);

        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var run = probe.MethodsList.Single(m => m.Name == "Run");

        Assert.NotNull(run.Body);
        return run.Body.Statements.OfType<LocalDeclarationStatement>().Last();
    }

    private ImmutableArray<Diagnostic> BuildRun(string statement, string extraTypes = "")
    {
        _ = BuildModel(statement, extraTypes, out var diagnostics);
        return diagnostics;
    }

    /// <summary>
    /// Wraps the statement in a method whose preamble supplies the typed locals and receiver the tests
    /// draw on, so a test states only the call it is about.
    /// </summary>
    private ProgramModelClass BuildModel(string statement, string extraTypes, out ImmutableArray<Diagnostic> diagnostics)
    {
        var source = $$"""
            namespace Probe;

            {{Fixtures}}

            {{extraTypes}}

            public class Probe
            {
                public static void Run()
                {
                    int i = 1;
                    bool b = true;
                    double d = 1.5;
                    string s = "text";
                    var p = new Printer();
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
    private ProgramModelClass Build(string source, out ImmutableArray<Diagnostic> diagnostics)
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
        }
    }
}