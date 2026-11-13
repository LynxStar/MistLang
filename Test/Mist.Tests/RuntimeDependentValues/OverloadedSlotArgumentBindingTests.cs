using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "3")]
public sealed class OverloadedSlotArgumentBindingTests : ProgramModelProbe
{
    public OverloadedSlotArgumentBindingTests()
        : base("MistOverloadedSlots_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string Meter = """
        public class Box<N:int>
        {
        }

        public class Meter
        {
            public int Foo<N:int>(int x) { return N + x; }
            public int Foo(int a, int x) { return a * 100 + x; }
            public static int Bar<N:int>(int x) { return N + x; }
            public static int Bar(int a, int x) { return a * 100 + x; }
            public int Pick<T>(T x) { return 1; }
            public int Pick<N:int>(int x) { return 2; }
            public static int Choose<T>(T x) { return 1; }
            public static int Choose<N:int>(int x) { return 2; }
            public int Baz<N:int>(int x) { return 1; }
            public int Baz<N:int>(string x) { return 2; }
            public static int Qux<N:int>(int x) { return 1; }
            public static int Qux<N:int>(string x) { return 2; }
            public int Pos<N:int>(int x) where N >= 1 { return N; }
            public int Pos(int a, int x) { return a; }
            public static int Neg<N:int>(int x) where N <= -1 { return N; }
            public static int Neg(int a, int x) { return a; }
            public int Spread<params Ts>(string x) { return 1; }
            public int Spread<N:int>(int x) { return 2; }
            public static int Fan<params Ts>(string x) { return 1; }
            public static int Fan<N:int>(int x) { return 2; }
            public int Wide<N:int>(int x) { return N + x; }
            public int Wide<params Ts>(string x) { return 0; }
            public static int Broad<N:int>(int x) { return N + x; }
            public static int Broad<params Ts>(string x) { return 0; }
        }
        """;

    [Fact]
    public void AStatedValueBindsTheSlottedOverloadBesideOneThatTakesNoList()
    {
        var calls = Calls("var a = m.Foo<n>(1); var b = Meter.Bar<n>(1); var c = m.Foo<p>(1); var d = Meter.Bar<p>(1);", out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.All(calls, call => Assert.Single(call.Method!.TypeParametersList));
        Assert.Equal("n", Assert.IsType<LocalReferenceExpression>(CaptureOf(calls[0]).Initializer).Local.Name);
        Assert.Equal("n", Assert.IsType<LocalReferenceExpression>(CaptureOf(calls[1]).Initializer).Local.Name);
        Assert.Equal("p", Assert.IsType<LocalReferenceExpression>(CaptureOf(calls[2]).Initializer).Local.Name);
        Assert.Equal("p", Assert.IsType<LocalReferenceExpression>(CaptureOf(calls[3]).Initializer).Local.Name);
    }

    [Fact]
    public void AnOverloadThatTakesNoListIsNeverChosenByAStatedValue()
    {
        var bound = Initializers("var a = m.Foo<n>(1, 2); var b = Meter.Bar<n>(1, 2);", out var diagnostics);

        Assert.All(bound, expression => Assert.IsType<ErrorExpression>(expression));
        Assert.Equal(
            ["MIST0393 method 'Foo'", "MIST0393 method 'Bar'"],
            Errors(diagnostics).Select(error => $"{error.Number.Value} {error.Message.Split(" states")[0]["The call to ".Length..]}"));
    }

    [Fact]
    public void EachCandidateReadsTheListByTheKindOfItsOwnSlot()
    {
        var calls = Calls("var a = m.Pick<n>(1); var b = m.Pick<int>(1); var c = Meter.Choose<n>(1); var d = Meter.Choose<int>(1);", out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.True(calls[0].Method!.TypeParametersList[0].IsDependent);
        Assert.False(calls[1].Method!.TypeParametersList[0].IsDependent);
        Assert.True(calls[2].Method!.TypeParametersList[0].IsDependent);
        Assert.False(calls[3].Method!.TypeParametersList[0].IsDependent);
        Assert.Equal("n", CaptureOf(calls[0]).Name);
        Assert.Empty(calls[1].Captures);
        Assert.Equal("n", CaptureOf(calls[2]).Name);
        Assert.Empty(calls[3].Captures);
    }

    [Fact]
    public void ANameThatIsNeitherATypeNorAValueIsReportedOnceAcrossItsReadings()
    {
        _ = Initializers(
            "var a = m.Foo<zzz>(1); var b = Meter.Bar<yyy>(1); var c = m.Pick<xxx>(1); var d = Meter.Choose<www>(1);", out var diagnostics);

        Assert.Equal(
            ["MIST0298 zzz", "MIST0298 yyy", "MIST0298 xxx", "MIST0298 www"],
            Errors(diagnostics).Select(error => $"{error.Number.Value} {error.Message.Split('\'')[1]}"));
    }

    [Fact]
    public void AStatedValueNoCandidateCanTakeIsReportedOnceAndNotAsAType()
    {
        var bound = Initializers("var a = m.Pick<n>(1, 2); var b = Meter.Choose<n>(1, 2);", out var diagnostics);

        Assert.All(bound, expression => Assert.IsType<ErrorExpression>(expression));
        Assert.Equal(["MIST0385", "MIST0385"], Errors(diagnostics).Select(error => error.Number.Value));
    }

    [Fact]
    public void TheCaptureIsMadeOnceOnTheChosenInvocation()
    {
        var calls = Calls("var a = m.Baz<n>(\"t\"); var b = Meter.Qux<n>(\"t\");", out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.All(calls, call => Assert.Equal("string", call.ParameterTypes![0].Name, StringComparer.OrdinalIgnoreCase));
        Assert.Equal("n", CaptureOf(calls[0]).Name);
        Assert.Equal("n", CaptureOf(calls[1]).Name);
    }

    [Fact]
    public void AStatedPathThatDoesNotBindIsReportedOnceAcrossTheCandidatesShapes()
    {
        _ = Initializers("var a = m.Baz<s.Nope>(1); var b = Meter.Qux<s.Nope>(1);", out var diagnostics);

        Assert.Equal(2, Errors(diagnostics).Count);
        Assert.All(Errors(diagnostics), error => Assert.Contains("Nope", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void TheSlotsContractRidesOnTheCaptureAndALiteralBindsAsBefore()
    {
        var calls = Calls("var a = m.Pos<n>(1); var b = Meter.Neg<n>(1); var c = m.Foo<4>(1); var d = Meter.Bar<4>(1);", out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Same(calls[0].Method!.TypeParametersList[0].Refinement, Assert.IsType<RefinedType>(CaptureOf(calls[0]).Type).Predicate);
        Assert.Same(calls[1].Method!.TypeParametersList[0].Refinement, Assert.IsType<RefinedType>(CaptureOf(calls[1]).Type).Predicate);
        Assert.Equal(4, Convert.ToInt32(Assert.IsType<LiftedType>(Assert.IsType<MemberAccessExpression>(calls[2].Receiver).TypeArguments[0]).Value));
        Assert.Equal(4, Convert.ToInt32(Assert.IsType<LiftedType>(Assert.IsType<MemberAccessExpression>(calls[3].Receiver).TypeArguments[0]).Value));
        Assert.Empty(calls[2].Captures);
        Assert.Empty(calls[3].Captures);
    }

    [Fact]
    public void ASpreadBesideASlottedOverloadFillsOnlyAPackSlot()
    {
        var calls = Calls("var a = m.Spread<Ts...>(s); var b = Meter.Fan<Ts...>(s);", out var diagnostics, "Run<params Ts>(int p)");

        Assert.Empty(Errors(diagnostics));
        Assert.All(calls, call => Assert.True(call.Method!.TypeParametersList[0].IsPack));
        Assert.All(calls, call => Assert.Empty(call.Captures));
    }

    [Fact]
    public void ASpreadInAFixedSlotBesideASlottedOverloadIsRefusedAsWithoutIt()
    {
        var bound = Initializers("var a = m.Pick<Ts...>(1); var b = Meter.Choose<Ts...>(1);", out var diagnostics, "Run<params Ts>(int p)");

        Assert.Equal(
            ["MIST0421 Pick", "MIST0421 Choose"],
            Errors(diagnostics).Select(error => $"{error.Number.Value} {error.Message.Split('\'')[3]}"));
        Assert.All(bound, expression => Assert.True(PriorFailure.Of(expression)));
    }

    [Fact]
    public void AnOrdinarySlotBesideASlottedOverloadReadsANestedLiteralAsAType()
    {
        var calls = Calls("var a = m.Pick<Box<3>>(q); var b = Meter.Choose<Box<3>>(q);", out var diagnostics, "Run(int p, Box<3> q)");

        Assert.Empty(Errors(diagnostics));
        Assert.All(calls, call => Assert.False(call.Method!.TypeParametersList[0].IsDependent));
        Assert.All(calls, call => Assert.Empty(call.Captures));
    }

    [Fact]
    public void AnOrdinarySlotBesideASlottedOverloadCapturesANestedValueAsWithoutIt()
    {
        var calls = Calls("var a = m.Pick<Box<n>>(q); var b = Meter.Choose<Box<n>>(q);", out var diagnostics, "Run(int p, Box<3> q)");

        Assert.Empty(Errors(diagnostics));
        Assert.All(calls, call => Assert.False(call.Method!.TypeParametersList[0].IsDependent));
        Assert.All(calls, call => Assert.Same(
            NestedCaptureOf(call).Value,
            Assert.IsType<ConstructedType>(Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments[0]).TypeArguments[0]));
    }

    [Fact]
    public void BesideAPackOverloadTheSharedReadingCapturesANestedValue()
    {
        var calls = Calls("var a = m.Wide<Box<n>>(s); var b = Meter.Broad<Box<n>>(s);", out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.All(calls, call => Assert.True(call.Method!.TypeParametersList[0].IsPack));
        Assert.All(calls, call => _ = NestedCaptureOf(call));
    }

    [Fact]
    public void AStatedValueBindsTheSlottedOverloadBesideAPackOverload()
    {
        var calls = Calls("var a = m.Wide<n>(1); var b = Meter.Broad<n>(1); var c = m.Wide<int>(s); var d = Meter.Broad<int>(s);", out var diagnostics);

        Assert.Empty(Errors(diagnostics));
        Assert.Equal("n", CaptureOf(calls[0]).Name);
        Assert.Equal("n", CaptureOf(calls[1]).Name);
        Assert.All(calls[2..], call => Assert.True(call.Method!.TypeParametersList[0].IsPack));
        Assert.All(calls[2..], call => Assert.Empty(call.Captures));
    }

    [Fact]
    public void BesideAPackOverloadANameThatIsNeitherIsReportedOnceAndAValueNoneTakesNotAsAType()
    {
        var bound = Initializers(
            "var a = m.Wide<zzz>(1); var b = Meter.Broad<yyy>(1); var c = m.Wide<n>(1, 2); var d = Meter.Broad<n>(1, 2); var e = m.Wide<n>(s); var f = Meter.Broad<n>(s);",
            out var diagnostics);

        Assert.Equal(
            ["MIST0298 zzz", "MIST0298 yyy", "MIST0385", "MIST0385"],
            Errors(diagnostics).Select(error => error.Number.Value == "MIST0298" ? $"{error.Number.Value} {error.Message.Split('\'')[1]}" : error.Number.Value));
        Assert.All(bound[2..4], expression => Assert.IsType<ErrorExpression>(expression));
        Assert.All(bound[4..], expression => Assert.True(Assert.IsType<InvocationExpression>(expression).Method!.TypeParametersList[0].IsDependent));
    }

    private List<InvocationExpression> Calls(string statements, out ImmutableArray<Diagnostic> diagnostics, string signature = "Run(int p)")
    {
        return [.. Initializers(statements, out diagnostics, signature).Select(Assert.IsType<InvocationExpression>)];
    }

    private List<Expression> Initializers(string statements, out ImmutableArray<Diagnostic> diagnostics, string signature = "Run(int p)")
    {
        var model = Build(
            $$"""
            namespace Probe;

            {{Meter}}

            public class Probe
            {
                public static void {{signature}}
                {
                    var n = 3;
                    var s = "s";
                    var m = new Meter();
                    {{statements}}
                }
            }
            """,
            out diagnostics);

        return [.. Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>().Skip(3).Select(declaration => declaration.Initializer!)];
    }

    private static SlotCapture CaptureOf(InvocationExpression call)
    {
        var capture = Assert.Single(call.Captures);
        Assert.Same(capture.Value, Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments[0]);
        return capture;
    }

    private static SlotCapture NestedCaptureOf(InvocationExpression call)
    {
        var capture = Assert.Single(call.Captures);
        Assert.Equal("n", Assert.IsType<LocalReferenceExpression>(capture.Initializer).Local.Name);
        return capture;
    }

    private static List<Diagnostic> Errors(ImmutableArray<Diagnostic> diagnostics)
    {
        return [.. diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }
}