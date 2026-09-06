// Tier 3 (Component): what `new(...)` binds to, asserted against a real ProgramModelBuilder run over
// minimal source. No Roslyn and no process spawning.
//
// Asserted on the MODEL rather than on output for the reason OverloadSelectionTests gives at length:
// emission re-spells a construction and lets Roslyn resolve it again, so a program printing the right
// answer is equally consistent with Mist having chosen nothing. It bites harder here, because C# has
// target-typed new of its own -- emitting the type we resolved is the only thing that distinguishes
// our answer from Roslyn's.

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
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using CompilationUnit = Mist.Driver.Contracts.CompilationUnit;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

public sealed class TargetTypedNewTests : IDisposable
{
    private const string NoTargetId = "MIST_PM_TARGET_TYPED_NEW_NO_TARGET";
    private const string NotConstructibleId = "MIST_PM_TARGET_TYPE_NOT_CONSTRUCTIBLE";

    private const string Fixtures = """
        public class Point
        {
            public int X;

            public Point(int x) { X = x; }
        }

        public class Tag
        {
            public Tag(int number) { }

            public Tag(string label) { }

            public Tag(int first, int second) { }
        }

        public class Holder
        {
            public Tag Inner;

            public Holder(Tag inner) { Inner = inner; }
        }

        public class Sink
        {
            public static int Take(Point p) { return 1; }

            public static int Wide(Point p) { return 1; }

            public static int Wide(Tag t) { return 2; }
        }
        """;

    private readonly string outputDir;

    public TargetTypedNewTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistTargetNew_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void AnExplicitlyTypedLocal_SuppliesTheTarget()
    {
        var creation = SingleNew("Point made = new(1);");

        Assert.Equal("Probe.Point", creation.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnAssignmentToAnExistingLocal_SuppliesTheTarget()
    {
        var model = BuildModel("Point first = new(1); first = new(2);", "", out _);
        var assignment = Assert.IsType<AssignmentExpression>(LastExpressionStatement(model).Expression);
        var creation = Assert.IsType<NewExpression>(assignment.Value);

        Assert.Equal("Probe.Point", creation.Type.FullyQualifiedName);
    }

    [Fact]
    public void AReturnStatement_SuppliesTheTarget()
    {
        var model = BuildModel(
            "var unused = 0;",
            extraTypes: """
                public class Maker
                {
                    public static Point Make() { return new(3); }
                }
                """,
            out _);

        var make = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Maker"]).MethodsList.Single(m => m.Name == "Make");
        var returned = make.Body!.Statements.OfType<ReturnStatement>().Single();
        var creation = Assert.IsType<NewExpression>(returned.Value);

        Assert.Equal("Probe.Point", creation.Type.FullyQualifiedName);
    }

    [Fact]
    public void AFieldInitializer_SuppliesTheTarget()
    {
        var model = BuildModel(
            "var unused = 0;",
            extraTypes: "public class Box { public Point Origin = new(4); }",
            out _);

        var field = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Box"]).FieldsList.Single(f => f.Name == "Origin");
        var creation = Assert.IsType<NewExpression>(field.Initializer);

        Assert.Equal("Probe.Point", creation.Type.FullyQualifiedName);
    }

    [Fact]
    public void AParameterAtAnArgumentPosition_SuppliesTheTarget()
    {
        var invocation = SingleInvocation("var taken = Sink.Take(new(5));");
        var creation = Assert.IsType<NewExpression>(Assert.Single(invocation.Arguments));

        Assert.Equal("Probe.Point", creation.Type.FullyQualifiedName);
    }

    [Fact]
    public void AConstructedGeneric_SuppliesTheTarget()
    {
        var creation = SingleNew(
            "Pair<int> paired = new(1, 2);",
            extraTypes: """
                public class Pair<T>
                {
                    public Pair(T first, T second) { }
                }
                """);

        Assert.Equal("Probe.Pair<Int32>", creation.Type.FullyQualifiedName);
    }

    /// <summary>
    /// The nested case is what distinguishes a target type SCOPED to the argument slot from one merely
    /// left in place: the outer target is Holder, and an inner new that inherited it would construct a
    /// second Holder rather than the Tag its parameter asks for.
    /// </summary>
    [Fact]
    public void ANestedTargetTypedNew_TakesTheParameterTypeNotTheOuterTarget()
    {
        var outer = SingleNew("Holder held = new(new(6));");
        var inner = Assert.IsType<NewExpression>(Assert.Single(outer.Arguments));

        Assert.Equal("Probe.Holder", outer.Type.FullyQualifiedName);
        Assert.Equal("Probe.Tag", inner.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Tag t = new(7);", "Standard.Int32")]
    [InlineData("Tag t = new(\"seven\");", "Standard.String")]
    public void TheConstructorIsSelectedByArgumentType(string statement, string expectedParameterType)
    {
        var creation = SingleNew(statement);

        Assert.NotNull(creation.Constructor);
        var parameter = Assert.Single(creation.Constructor.ParametersList);
        Assert.Equal(expectedParameterType, parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void TheConstructorIsSelectedByArity()
    {
        var creation = SingleNew("Tag t = new(1, 2);");

        Assert.NotNull(creation.Constructor);
        Assert.Equal(2, creation.Constructor.ParametersList.Count);
    }

    /// <summary>
    /// The two shapes that leave nothing to target. A <c>var</c> has no declared type to be the target,
    /// and an argument to a call with several candidates of the right arity has no single parameter to
    /// be it -- picking one would need the argument types, and this argument is one of them.
    /// </summary>
    [Theory]
    [InlineData("var loose = new(1);")]
    [InlineData("var wide = Sink.Wide(new(1));")]
    public void WithNothingToTarget_TheConstructionIsRefused(string statement)
    {
        var diagnostics = BuildRun(statement);

        _ = Assert.Single(Errors(diagnostics, NoTargetId));
    }

    /// <summary>
    /// A type parameter reaches the target position without being a class or a struct, which is the
    /// case C# also refuses absent a <c>new()</c> constraint.
    /// </summary>
    [Fact]
    public void ATargetNothingCanConstruct_IsRefused()
    {
        var diagnostics = BuildRun(
            "var unused = 0;",
            extraTypes: """
                public class Forge
                {
                    public static void Make<T>() { T made = new(); }
                }
                """);

        var reported = Assert.Single(Errors(diagnostics, NotConstructibleId));
        Assert.Contains("T", reported.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Point made = new(1);")]
    [InlineData("Tag t = new(1, 2);")]
    [InlineData("Holder held = new(new(6));")]
    [InlineData("var taken = Sink.Take(new(5));")]
    public void AWellFormedTargetTypedNew_IsNotRefused(string statement)
    {
        var diagnostics = BuildRun(statement);

        Assert.Empty(Errors(diagnostics, NoTargetId));
        Assert.Empty(Errors(diagnostics, NotConstructibleId));
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static IEnumerable<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value == id);
    }

    private NewExpression SingleNew(string statement, string extraTypes = "")
    {
        return Assert.IsType<NewExpression>(InitializerOf(statement, extraTypes));
    }

    private InvocationExpression SingleInvocation(string statement, string extraTypes = "")
    {
        return Assert.IsType<InvocationExpression>(InitializerOf(statement, extraTypes));
    }

    private Expression InitializerOf(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, out _);
        var declaration = Run(model).Body!.Statements.OfType<LocalDeclarationStatement>().Last();

        Assert.NotNull(declaration.Initializer);
        return declaration.Initializer;
    }

    private static ExpressionStatement LastExpressionStatement(ProgramModelClass model)
    {
        return Run(model).Body!.Statements.OfType<ExpressionStatement>().Last();
    }

    private static Method Run(ProgramModelClass model)
    {
        return Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
    }

    private ImmutableArray<Diagnostic> BuildRun(string statement, string extraTypes = "")
    {
        _ = BuildModel(statement, extraTypes, out var diagnostics);
        return diagnostics;
    }

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
                    {{statement}}
                }
            }
            """;

        return Build(source, out diagnostics);
    }

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
        public void BridgeWellKnownMembers(ProgramModelClass programModel)
        {
            // No-op: the stub registers shells only and loads no CLR assembly.
        }

    }
}