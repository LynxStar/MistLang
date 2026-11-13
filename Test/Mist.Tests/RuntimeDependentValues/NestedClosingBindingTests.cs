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

[Trait("Tier", "3")]
public sealed class NestedClosingBindingTests : ProgramModelProbe
{
    public NestedClosingBindingTests()
        : base("MistNestedClosings_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Types = """
        public class Ring<Capacity:int>
        {
        }

        public class Bag<T>
        {
        }

        public class Counter<T>
        {
            public static int Read() { return 1; }
            public static int Get<U>() { return 2; }
        }

        public class Holder<N:int>
        {
            public static int Read() { return N; }
        }
        """;

    [Fact]
    public void AStatedMethodArgumentCapturesTheValueOfAClosingNestedInIt()
    {
        var calls = Statements("""
            var bag = new Bag<Ring<n>>();
            var probe = new Probe();
            var a = First<Ring<n>>(bag);
            var b = Probe.First<Ring<n>>(bag);
            var c = probe.Inst<Ring<n>>(bag);
            var d = Counter<int>.Get<Ring<n>>();
            """, out var diagnostics).OfType<LocalDeclarationStatement>().Skip(2).Select(declaration => Assert.IsType<InvocationExpression>(declaration.Initializer)).ToList();

        Assert.Empty(Errors(diagnostics));
        Assert.Equal(4, calls.Count);
        Assert.All(calls, call => Assert.Same(CaptureOfN(call.Captures).Value, Slot(Stated(call)[0])));
    }

    [Fact]
    public void ACallThroughAClosedTypeOwnsTheCapturesOfThatClosing()
    {
        var calls = Statements("""
            var a = Counter<Ring<n>>.Read();
            var b = Holder<n>.Read();
            """, out var diagnostics).Select(statement => Assert.IsType<InvocationExpression>(Assert.IsType<LocalDeclarationStatement>(statement).Initializer)).ToList();

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(calls[0].Captures).Value, Nested(Through(calls[0])));
        Assert.Same(CaptureOfN(calls[1].Captures).Value, Slot(Through(calls[1])));
    }

    [Fact]
    public void AnUninitializedLocalAndAForInitializerOwnTheCapturesOfTheirWrittenTypes()
    {
        var statements = Statements("""
            Bag<Ring<n>> held;
            for(Ring<n> r = new Ring<n>(); false; )
            {
            }
            """, out var diagnostics);
        var held = Assert.IsType<LocalDeclarationStatement>(statements[0]);
        var initializer = Assert.IsType<LocalDeclarationStatement>(Assert.IsType<ForStatement>(statements[1]).Initializer);

        Assert.Empty(Errors(diagnostics));
        Assert.Same(CaptureOfN(held.Captures).Value, Nested(held.Local.Type));
        Assert.Same(CaptureOfN(initializer.Captures).Value, Slot(initializer.Local.Type));
        Assert.NotSame(initializer.Captures[0], Assert.Single(Assert.IsType<NewExpression>(initializer.Initializer).Captures));
    }

    [Fact]
    public void ABareTypePatternOwnsTheCapturesOfItsTestedType()
    {
        var statements = Statements("""
            unknown u = new Ring<n>();
            var a = u is Ring<n>;
            var b = u switch { Ring<n> => 1, _ => 0 };
            """, out var diagnostics);
        var bare = Assert.IsType<TypeTestPattern>(Assert.IsType<IsPatternExpression>(Assert.IsType<LocalDeclarationStatement>(statements[1]).Initializer).Pattern);
        var arm = Assert.IsType<TypeTestPattern>(Assert.IsType<SwitchExpression>(Assert.IsType<LocalDeclarationStatement>(statements[2]).Initializer).Arms[0].Pattern);

        Assert.Empty(Errors(diagnostics));
        Assert.All(new[] { bare, arm }, test => Assert.Same(CaptureOfN(test.Captures).Value, Slot(test.NarrowedType)));
    }

    [Fact]
    public void AValuesNameInTheSamePositionsOfASignatureIsStillRefused()
    {
        _ = Build(
            $$"""
            namespace Probe;

            {{Types}}

            public class Probe
            {
                public static void Listed(int n, Bag<Ring<n>> items)
                {
                }

                public static void Counted(int n, Counter<Ring<n>> counter)
                {
                }
            }
            """,
            out var diagnostics);

        Assert.Equal(
            ["MIST0487 n", "MIST0487 n"],
            Errors(diagnostics).Select(error => $"{error.Number.Value} {error.Message.Split('\'')[1]}"));
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
                public int Inst<T>(Bag<T> items) { return 1; }

                public static void Run()
                {
                    var n = 3;
                    {{statements}}
                }
            }
            """,
            out diagnostics);

        return [.. Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!.Statements.Skip(1)];
    }

    private static SlotCapture CaptureOfN(IReadOnlyList<SlotCapture> captures)
    {
        var capture = Assert.Single(captures);
        Assert.Equal("n", Assert.IsType<LocalReferenceExpression>(capture.Initializer).Local.Name);
        return capture;
    }

    private static IMistType Slot(IMistType type)
    {
        return Assert.IsType<ConstructedType>(type).TypeArguments[0];
    }

    private static IMistType Nested(IMistType type)
    {
        return Slot(Slot(type));
    }

    private static IReadOnlyList<IMistType> Stated(InvocationExpression call)
    {
        return call.Receiver switch
        {
            MemberAccessExpression access => access.TypeArguments,
            MethodReferenceExpression reference => reference.TypeArguments,
            _ => throw new InvalidOperationException($"No stated arguments on a {call.Receiver.GetType().Name}."),
        };
    }

    private static IMistType Through(InvocationExpression call)
    {
        return Assert.IsType<TypeReferenceExpression>(Assert.IsType<MemberAccessExpression>(call.Receiver).Receiver).ReferencedType;
    }

    private static List<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics)
    {
        return [.. diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}