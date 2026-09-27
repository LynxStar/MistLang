// Tier 3 (Component): the promises a scope reads off something other than the body that keeps them -- a
// struct copy's owned reference field, an abstract accessor or default body, an owned slot, an erased
// type test, a readonly type's settable property, an initializer lambda, a params element, an implicit
// clone and an implicit constructor -- each pinned where the promise is judged. Every fixture type is
// under analysis in every row, so a Clone that counts itself and a getter that ticks a clock are
// silences the rows prove until a declared promise reaches them.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PuritySoundnessRoundTwoTests : ProgramModelProbe
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string ContractNotMet = "MIST_PA_PURITY_CONTRACT_NOT_MET";
    private const string Untestable = "MIST_PA_PURE_FUNCTION_TYPE_UNTESTABLE";
    private const string CallsMutable = "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE";
    private const string SlotReturnNotOwned = "MIST_PA_SLOT_RETURN_NOT_OWNED";

    private static readonly string[] Ids =
    [
        Writes, ReadsMutable, CallsImpure, "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_PURE_SCOPE_STORES_UNOWNED",
        "MIST_PA_PURE_FUNCTION_REQUIRED", "MIST_PA_DEFAULT_NOT_CONSISTENT", ContractNotMet, Untestable, CallsMutable,
        SlotReturnNotOwned, "MIST_PA_SLOT_SIGNATURE_MISMATCH", "MIST_PA_SLOT_NOT_FILLED", "MIST_PA_OWNED_RETURN_NOT_OWNED",
        "MIST_PA_READONLY_CONTRACT_NOT_MET",
    ];

    private const string OwnedFieldCell = """
        public struct Cell
        {
            public owned Box Inner;

            public pure Cell(owned Box inner) { Inner = inner; }

        """;

    public PuritySoundnessRoundTwoTests()
        : base("MistPuritySoundnessRoundTwo_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Clock
        {
            public static int Ticks;
        }

        public class Box
        {
            public int N;

            public pure Box() { }
        }

        public struct Pair
        {
            public int A;

            public Pair(int a) { A = a; }
        }

        public class Loud
        {
            public static int Made;
            public int N;

            public pure Loud() { }

            public owned Loud Clone() { Made = Made + 1; var c = new Loud(); c.N = N; return c; }
        }

        public class Quiet
        {
            public int N;

            public pure Quiet() { }

            public pure Quiet(int n) { N = n; }

            public owned Quiet Clone() { return new Quiet(N); }
        }

        public class Pool
        {
            public static readonly Quiet Shared = new Quiet();
        }

        type Reader = pure func<int>;

        public class Ops
        {
            public static T Coerce<T>(unknown u, T fallback) { if(u is T t) { return t; } return fallback; }

            public static T? Soft<T>(unknown u) { return u as T; }

            public static Reader Pick(params Reader[] rs) { foreach(var r in rs) { return r; } return () => 0; }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AStructsLocalMember_MayNotWriteThroughAnOwnedReferenceFieldOfTheCopy()
    {
        var finding = Assert.Single(Analyse("", OwnedFieldCell + "public local void Bump() { Inner.N = 42; }\n}"));

        Assert.Equal(Writes, finding.Id.Value);
        Assert.Contains("'Bump'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructsLocalReadonlyMember_MayNotReadThroughAnOwnedReferenceFieldOfTheCopy()
    {
        var finding = Assert.Single(Analyse("", OwnedFieldCell + "public local readonly int Look() { return Inner.N; }\n}"));

        Assert.Equal(ReadsMutable, finding.Id.Value);
        Assert.Contains("'Look'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static pure int Poke(Cell c) { c.Bump(); return 0; }", CallsImpure)]
    [InlineData("public static readonly int Poke(Cell c) { c.Bump(); return 0; }", CallsMutable)]
    public void AnUnmarkedStructMemberWritingThroughAnOwnedReferenceField_IsNotLocalToTheHolderOfACopy(string member, string expected)
    {
        var finding = Assert.Single(Analyse("", OwnedFieldCell + "public void Bump() { Inner.N = 42; }\n} public class Prober { " + member + " }"));

        Assert.Equal(expected, finding.Id.Value);
        Assert.Contains("Bump", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructsLocalMember_MayStillWriteAndReadTheCopysOwnValueFields()
    {
        Assert.Empty(Analyse(
            "",
            "public struct Cell { public int Count; public pure Cell(int c) { Count = c; } public local void Bump() { Count = Count + 1; } public local readonly int Look() { return Count; } } "
            + "public class Prober { public static pure int Poke(Cell c) { c.Bump(); return c.Look(); } }"));
    }

    [Theory]
    [InlineData("public static pure int Measure(IShape s) { return s.Area; }", CallsImpure)]
    [InlineData("public static readonly int Measure(IShape s) { return s.Area; }", CallsMutable)]
    public void AnAbstractAutoGetter_KeepsOnlyWhatEveryImplementationKeeps(string member, string expected)
    {
        var finding = Assert.Single(Analyse(
            "",
            "public abstract IShape { int Area { get; } } public class Sq : IShape { public int Area { get { Clock.Ticks = Clock.Ticks + 1; return Clock.Ticks; } } } "
            + "public class Prober { " + member + " }"));

        Assert.Equal(expected, finding.Id.Value);
        Assert.Contains("Area", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractAutoSetter_KeepsOnlyWhatEveryImplementationKeeps()
    {
        var finding = Assert.Single(Analyse(
            "",
            "public abstract IShape { int Area { get; set; } } public class Sq : IShape { public pure Sq() { } public int Area { get { return 1; } set { Clock.Ticks = Clock.Ticks + value; } } } "
            + "public class Prober { public static pure int Fill() { owned IShape s = new Sq(); s.Area = 5; return s.Area; } }"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("'Fill'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractAutoGetter_EveryImplementationOfWhichIsPure_StaysPure()
    {
        Assert.Empty(Analyse(
            "",
            "public abstract IShape { int Area { get; } } public class Sq : IShape { public readonly int side; public pure Sq(int s) { side = s; } public int Area { get { return side * side; } } } "
            + "public class Prober { public static pure int Measure(IShape s) { return s.Area; } }"));
    }

    [Fact]
    public void AnAbstractDefaultBody_IsMetWithEveryReplacement()
    {
        var finding = Assert.Single(Analyse(
            "",
            "public abstract IShape { int Area() { return 1; } } public class Sq : IShape { public int Area() { Clock.Ticks = Clock.Ticks + 1; return Clock.Ticks; } } "
            + "public class Prober { public static pure int Measure(IShape s) { return s.Area(); } }"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("'Area'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Sq : IShape { }")]
    [InlineData("public class Sq : IShape { public int Area() { return 4; } }")]
    public void AnAbstractDefaultBody_KeptByEveryConformer_StaysPure(string conformer)
    {
        Assert.Empty(Analyse(
            "",
            "public abstract IShape { int Area() { return 1; } } " + conformer
            + " public class Prober { public static pure int Measure(IShape s) { return s.Area(); } }"));
    }

    [Fact]
    public void AnOwnedSlot_IsAnsweredOnlyByAnOwnedReturn()
    {
        var finding = Assert.Single(Analyse(
            "",
            "public abstract IMaker { pure owned Box Make(); } public class Maker : IMaker { public pure Box Make() { return Store.Kept; } } public class Store { public static readonly Box Kept = new Box(); } "
            + "public class Prober { public static pure int Poke(IMaker m) { var b = m.Make(); b.N = b.N + 1; return b.N; } }"));

        Assert.Equal(SlotReturnNotOwned, finding.Id.Value);
        Assert.Contains("Maker", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnedSlotAnsweredByAnOwnedReturn_IsOwnedByAPureCaller()
    {
        Assert.Empty(Analyse(
            "",
            "public abstract IMaker { pure owned Box Make(); } public class Maker : IMaker { public pure owned Box Make() { return new Box(); } } "
            + "public class Prober { public static pure int Poke(IMaker m) { var b = m.Make(); b.N = b.N + 1; return b.N; } }"));
    }

    [Theory]
    [InlineData("func<int> plain = () => 1; Reader safe = () => 0; Reader r = Ops.Coerce<Reader>(plain, safe);")]
    [InlineData("func<int> plain = () => 1; var maybe = Ops.Soft<Reader>(plain);")]
    public void AGenericTypeTest_InstantiatedWithAPureFunctionType_IsUntestable(string statement)
    {
        var finding = Assert.Single(Analyse(statement, ""));

        Assert.Equal(Untestable, finding.Id.Value);
        Assert.Contains("pure func<", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var n = Ops.Coerce<int>(3, 4);")]
    [InlineData("var m = Ops.Soft<string>(\"text\");")]
    public void AGenericTypeTest_InstantiatedWithoutAPureFunctionType_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement, ""));
    }

    [Theory]
    [InlineData("public readonly class R { public int X { get; set; } public R(int x) { X = x; } }")]
    [InlineData("public readonly class R { public int X { get { return field; } set { field = value; } } public R(int x) { X = x; } }")]
    public void AReadonlyTypesSettablePropertyStorage_IsNotStable(string declaration)
    {
        var finding = Assert.Single(Analyse("", declaration + " public class Prober { public static pure int Peek(R r) { return r.X; } }"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("'Peek'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public readonly class R { public int X { get; set; } public R(int x) { X = x; } }", "public static pure int Peek(frozen R r) { return r.X; }")]
    [InlineData("public readonly class R { public int X { get; init; } public R(int x) { X = x; } }", "public static pure int Peek(R r) { return r.X; }")]
    public void AReadonlyTypesProperty_IsReadableWhereItsStorageCannotChangeUnderTheReader(string declaration, string member)
    {
        Assert.Empty(Analyse("", declaration + " public class Prober { " + member + " }"));
    }

    [Theory]
    [InlineData("public class Holder { public static readonly Reader R = () => { Clock.Ticks = 1; return 1; }; }")]
    [InlineData("public class Holder { public static Reader R { get; } = () => { Clock.Ticks = 1; return 1; }; }")]
    [InlineData("public class Holder { public readonly Reader R = () => { Clock.Ticks = 1; return 1; }; public Holder(int x) { } }")]
    public void AnInitializerThatIsALambda_IsJudgedWhereItConvertsToAPureFunctionType(string declaration)
    {
        var finding = Assert.Single(Analyse("", declaration));

        Assert.Equal(Writes, finding.Id.Value);
        Assert.Contains("lambda", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Holder { public static readonly Reader R = () => 1; }")]
    [InlineData("public class Holder { public readonly Reader R = () => 1; public Holder(int x) { } }")]
    [InlineData("public class Holder { public Reader R { get; set; } = () => 1; public Holder(int x) { } }")]
    public void AnInitializerThatIsAPureLambda_IsAdmittedWithoutFaultingTheInference(string declaration)
    {
        Assert.Empty(Analyse("", declaration));
    }

    [Fact]
    public void ALambdaPassedThroughAParamsParameter_IsJudgedAgainstTheElementType()
    {
        var finding = Assert.Single(Analyse("Reader r = Ops.Pick(() => { Clock.Ticks = 1; return 1; }, () => 1);", ""));

        Assert.Equal(Writes, finding.Id.Value);
        Assert.Contains("lambda", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PureLambdasPassedThroughAParamsParameter_AreAdmitted()
    {
        Assert.Empty(Analyse("Reader r = Ops.Pick(() => 1, () => 2);", ""));
    }

    [Fact]
    public void APureMethod_MayIterateItsOwnParamsArray()
    {
        Assert.Empty(Analyse(
            "",
            "public class Prober { public static pure int Sum(params Reader[] rs) { var total = 0; foreach(var r in rs) { total = total + r(); } return total; } }"));
    }

    [Fact]
    public void APureMethod_StillMayNotIterateAnOrdinaryArrayParameter()
    {
        var finding = Assert.Single(Analyse(
            "",
            "public class Prober { public static pure int Sum(Reader[] rs) { var total = 0; foreach(var r in rs) { total = total + r(); } return total; } }"));

        Assert.Equal(ReadsMutable, finding.Id.Value);
        Assert.Contains("'Sum'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImplicitCloneOfAFrozenInput_CallsTheClone()
    {
        var finding = Assert.Single(Analyse("", "public class Prober { public static pure int Take(frozen Loud b) { owned var c = b; c.N = 5; return c.N; } }"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("Clone", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImplicitCloneOfAnExternalObject_ReadsItsCurrentState()
    {
        var findings = Analyse("", "public class Prober { public static pure int Peek() { owned var c = Pool.Shared; return c.N; } }");

        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Contains(finding.Id.Value, new[] { CallsImpure, ReadsMutable }));
    }

    [Fact]
    public void AnImplicitCloneOfAFrozenInputThroughACloneThatReadsOnlyItsSource_IsAdmitted()
    {
        Assert.Empty(Analyse("", "public class Prober { public static pure int Take(frozen Quiet b) { owned var c = b; c.N = 5; return c.N; } }"));
    }

    [Theory]
    [InlineData("public abstract IGrid { pure int this[int i] { get; } } public class Grid : IGrid { public int this[int i] { get { return Clock.Ticks; } } }")]
    [InlineData("public abstract IGrid { local int this[int i] { get; set; } } public class Grid : IGrid { public int cell; public int this[int i] { get { return cell; } set { Clock.Ticks = value; } } }")]
    public void AnAbstractIndexersPromise_BindsEveryImplementation(string declarations)
    {
        var finding = Assert.Single(Analyse("", declarations));

        Assert.Equal(ContractNotMet, finding.Id.Value);
        Assert.Contains("Grid", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public abstract IGrid { pure int this[int i] { get; } } public class Grid : IGrid { public int this[int i] { get { return i * 2; } } }")]
    [InlineData("public abstract IGrid { local int this[int i] { get; set; } } public class Grid : IGrid { public int cell; public int this[int i] { get { return cell; } set { cell = value; } } }")]
    public void AnAbstractIndexersPromise_KeptByTheImplementation_IsAdmitted(string declarations)
    {
        Assert.Empty(Analyse("", declarations));
    }

    [Theory]
    [InlineData("public static pure int Direct() { var b = new Bare(); return b.N; }", "'Direct'")]
    [InlineData("public static int ViaImplicit() { var b = new Bare(); return b.N; } public static pure int Use() { return ViaImplicit(); }", "'Use'")]
    public void AnImplicitConstructorWhoseInitializerReadsAStatic_IsImpureToItsCaller(string members, string scope)
    {
        var finding = Assert.Single(Analyse("", "public class Bare { public int N = Clock.Ticks; public Pair P; } public class Prober { " + members + " }"));

        Assert.Contains(finding.Id.Value, new[] { CallsImpure, ReadsMutable });
        Assert.Contains(scope, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static pure int Direct() { var b = new Calm(); return b.N; }")]
    [InlineData("public static int ViaImplicit() { var b = new Calm(); return b.N; } public static pure int Use() { return ViaImplicit(); }")]
    public void AnImplicitConstructorWhoseInitializersArePure_KeepsTheAllocatingMemberPure(string members)
    {
        Assert.Empty(Analyse("", "public class Calm { public int N = 3; public Pair P; } public class Prober { " + members + " }"));
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))];
    }
}