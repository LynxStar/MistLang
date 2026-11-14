using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// Every place a body writes a type reads a value written in a dependent slot of it, and the node that wrote the
/// type owns the capture (runtime-dependent-values Requirement 5; MIST-1373): a typed foreach variable, a typed
/// deconstruction, a with binding, <c>default(T)</c>, the element type of an array creation or a stack allocation,
/// and a method group named as a value with stated arguments. A union test admits a tested type that differs from a
/// constituent only in a slot that is not a literal on both sides. A signature written in a body and a type value
/// stay refused, and a dependent parameter whose only source is a slot under <c>unknown</c> nested in another type's
/// argument is not inferable (Requirement 8.3).
/// </summary>
[Trait("Tier", "3")]
public sealed class BodySiteClosingBindingTests : ProgramModelProbe
{
    public BodySiteClosingBindingTests()
        : base("MistBodySiteClosings_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Types = """
        public class Ring<Capacity:int>
        {
        }

        public struct Cell<N:int>
        {
        }

        public class Bag<T>
        {
        }
        """;

    [Fact]
    public void ATypedForeachVariableOwnsTheCaptureOfItsType()
    {
        var loop = Assert.IsType<ForEachStatement>(Statements("""
            Ring<n>[] rings = [new Ring<n>()];
            foreach(Ring<n> r in rings)
            {
            }
            """, out var diagnostics)[1]);

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(loop.Captures).Value, Slot(loop.Variable.Type));
    }

    [Fact]
    public void ATypedDeconstructionOwnsTheCapturesOfItsTargetsTypes()
    {
        var deconstruction = Assert.IsType<TupleDeconstructionStatement>(Statements("(Ring<n> x, int y) = (new Ring<n>(), 1);", out var diagnostics)[0]);

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(deconstruction.Captures).Value, Slot(deconstruction.Targets[0].Type));
    }

    [Theory]
    [InlineData("with Ring<n> w = new Ring<n>();")]
    [InlineData("with Ring<n> _ = new Ring<n>();")]
    public void AWithBindingOwnsTheCaptureOfTheKeyItNames(string statement)
    {
        var ambient = Assert.IsType<WithBlockStatement>(Statements(statement, out var diagnostics)[0]);

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(ambient.Captures).Value, Slot(ambient.KeyType));
    }

    [Fact]
    public void ADefaultOwnsTheCaptureOfItsType()
    {
        var defaulted = Assert.IsType<DefaultExpression>(Initializer(Statements("var d = default(Ring<n>);", out var diagnostics)[0]));

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(defaulted.Captures).Value, Slot(defaulted.Type));
    }

    [Fact]
    public void AnArrayCreationOwnsTheCaptureOfItsElementTypeAheadOfItsLength()
    {
        var statements = Statements("""
            var fixedLength = new Cell<n>[3];
            var sized = new Cell<n>[n];
            """, out var diagnostics);
        var fixedLength = Assert.IsType<ArrayCreationExpression>(Initializer(statements[0]));
        var sized = Assert.IsType<ArrayCreationExpression>(Initializer(statements[1]));

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(fixedLength.Captures).Value, Slot(fixedLength.ElementType));
        Assert.Equal(2, sized.Captures.Count);
        Assert.Same(sized.Captures[0].Value, Slot(sized.ElementType));
        Assert.Same(sized.Size, sized.Captures[1].Initializer);
    }

    [Fact]
    public void AMethodGroupNamedAsAValueOwnsTheCapturesOfItsStatedArguments()
    {
        var statements = Statements("""
            func<Bag<Ring<n>>, int> bare = First<Ring<n>>;
            func<Bag<Ring<n>>, int> qualified = Probe.First<Ring<n>>;
            """, out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.All(statements, statement =>
        {
            var reference = Assert.IsType<FunctionReferenceExpression>(Initializer(statement));
            Assert.Same(CaptureOfN(reference.Captures).Value, Slot(reference.TypeArguments[0]));
        });
    }

    [Fact]
    public void AUnionTestAdmitsAClosingThatDiffersOnlyInARuntimeSlotAndRefusesAnotherLiteral()
    {
        var test = Assert.IsType<IsPatternExpression>(Initializer(Statements("""
            Ring<n> | string a = new Ring<n>();
            var found = a is Ring<n> x;
            """, out var admitted)[1]));

        _ = Statements("""
            Ring<5> | string b = new Ring<5>();
            var lost = b is Ring<7> y;
            """, out var refused);

        Assert.Empty(Errors(admitted));
        Assert.NotNull(Assert.IsType<TypeTestPattern>(test.Pattern).Constituent);
        Assert.Equal(["MIST0345"], Errors(refused).Select(error => error.Number.Value));
    }

    [Theory]
    [InlineData("int Local(Ring<n> r) { return 1; }")]
    [InlineData("Ring<n> Made() { return new Ring<n>(); }")]
    [InlineData("func<Ring<5>, int> f = (Ring<n> r) => 1;")]
    [InlineData("var t = Ring<n>;")]
    public void ASignatureWrittenInABodyAndATypeValueStillRefuseAValuesName(string statement)
    {
        _ = Statements(statement, out var diagnostics);

        Assert.Contains("MIST0487", Errors(diagnostics).Select(error => error.Number.Value));
    }

    [Fact]
    public void ASlotUnderUnknownNestedInAnArgumentIsNotInferableWhereADirectOneIs()
    {
        var direct = Assert.IsType<InvocationExpression>(Initializer(Statements("""
            Ring<unknown> r = new Ring<n>();
            var one = One(r);
            """, out var admitted)[1]));

        _ = Statements("""
            Bag<Ring<unknown>> written = new Bag<Ring<unknown>>();
            var listed = Len(written);
            var make = () => new Bag<Ring<n>>();
            var made = Len(make());
            var each = Made(() => new Ring<n>());
            """, out var refused);

        Assert.Empty(Errors(admitted));
        _ = Assert.Single(direct.Captures);
        Assert.Equal(
            ["MIST0490 Bag<Ring<unknown>>", "MIST0490 Bag<Ring<unknown>>", "MIST0490 func<Ring<unknown>>"],
            Errors(refused).Select(error => $"{error.Number.Value} {error.Message.Split('\'')[3]}"));
    }

    private List<Statement> Statements(string statements, out ImmutableArray<Diagnostic> diagnostics)
    {
        var model = Build(
            $$"""
            namespace Probe;

            {{Types}}

            public class Probe
            {
                public static int First<T>(Bag<T> items) { return 1; }
                public static int One<N:int>(Ring<N> ring) { return N; }
                public static int Len<N:int>(Bag<Ring<N>> rings) { return N; }
                public static int Made<N:int>(func<Ring<N>> make) { return N; }

                public static void Run()
                {
                    var n = 3;
                    {{statements}}
                }
            }
            """,
            out diagnostics);

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!;

        return [.. run.Statements.Skip(1)];
    }

    private static Expression? Initializer(Statement statement)
    {
        return Assert.IsType<LocalDeclarationStatement>(statement).Initializer is ImplicitConversionExpression conversion
            ? conversion.Operand
            : Assert.IsType<LocalDeclarationStatement>(statement).Initializer;
    }

    private static SlotCapture CaptureOfN(IReadOnlyList<SlotCapture> captures)
    {
        var capture = Assert.Single(captures);
        Assert.Equal("n", Assert.IsType<LocalReferenceExpression>(capture.Initializer).Local.Name);
        return capture;
    }

    private static IMistType Slot(IMistType type)
    {
        return Assert.Single(Assert.IsType<ConstructedType>(type).TypeArguments);
    }

    private static List<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics)
    {
        return [.. diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}