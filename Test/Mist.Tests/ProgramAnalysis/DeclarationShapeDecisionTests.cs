// Tier 3 (Component): what order 6's rules deliberately do NOT report, and the reach facts the corpus
// cannot phrase. The fixture types are themselves under analysis in every row, so the accesses their
// bodies make -- a private member from its own type and a nested one, a protected member from a
// derivation, an internal member across types of one compilation, a public class over an internal
// abstract -- are silences proven by every row. WHY each is silent is recorded once, on order 6 of
// backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class DeclarationShapeDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_INACCESSIBLE_MEMBER", "MIST_PA_INCONSISTENT_ACCESSIBILITY", "MIST_PA_SEALED_BASE_TYPE",
        "MIST_PA_NEWTYPE_OVER_SEALED_TYPE", "MIST_PA_SEALED_TYPE_ALIAS", "MIST_PA_ENUM_BASE_NOT_READONLY",
        "MIST_PA_ENUM_DUPLICATE_VALUE", "MIST_PA_DEFAULT_ENTRY_NOT_A_FIELD", "MIST_PA_CONSTRAINT_NOT_SATISFIED",
        "MIST_PA_CONSTRUCTOR_CONSTRAINT_NOT_SATISFIED", "MIST_PA_NOT_DEFAULTABLE",
        "MIST_PA_OPERATOR_NOT_PUBLIC", "MIST_PA_OPERATOR_UNPAIRED", "MIST_PA_LOGICAL_OPERATOR_DECLARED",
        "MIST_PA_OPERATOR_NOT_ON_DECLARING_TYPE", "MIST_PA_REFINEMENT_NOT_COMPARABLE", "MIST_PA_DUPLICATE_SIGNATURE", "MIST_PA_MEMBER_REDECLARATION",
    ];

    public DeclarationShapeDecisionTests()
        : base("MistDeclarationShape_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Owner
        {
            private int secret = 1;
            protected int guarded = 2;
            internal int shared = 3;
            public int open = 4;
            private static int Hidden() { return 1; }
            public int ReadOwn() { return secret + guarded + Hidden(); }

            public class Nested
            {
                public int Peek(Owner o) { return o.secret + o.guarded; }
            }
        }

        public class Derived : Owner
        {
            public int ReadGuarded() { return guarded; }

            public class Inner
            {
                public int Peek(Derived d) { return d.guarded; }
            }
        }

        public class Outsider
        {
            public int Read(Owner o) { return o.shared + o.open; }
        }

        internal class Unmarked
        {
            public int Get() { return 1; }
        }

        internal class ExposesInternally
        {
            public Unmarked Make() { return new Unmarked(); }
            public Unmarked holder = new Unmarked();
        }

        public class ExposesPrivately
        {
            private Unmarked hidden = new Unmarked();
            private Unmarked Make() { return hidden; }
        }

        abstract IShape
        {
            int Sides();
        }

        public class Square : IShape
        {
            public int Sides() { return 4; }
        }

        public sealed class Final
        {
        }

        type Renamed = Final;

        public class ReadonlyMethod
        {
            public readonly int Get() { return 1; }
        }

        public readonly class Frozen
        {
            public int v = 1;
        }

        public enum Distinct : int { A = 1, B = 2 }
        public enum Named : string { A = "a", B = "b" }
        public enum OverFrozen : Frozen { A = new Frozen() }

        public class Mutable
        {
            public int x = 1;
        }

        public class NeedsReadonly<T> where T : readonly { }
        public class NeedsMutable<T> where T : mutable { }
        public class NeedsCtor<T> where T : new(int) { }
        public class NeedsAny<T> where T : new() { }
        public class NeedsClass<T> where T : class { }
        public class NeedsStruct<T> where T : struct { }
        public class NeedsAffine<T> where T : affine { }
        public class NeedsShape<T> where T : IShape { }
        public class NeedsDefault<T> where T : default { }

        public class HasIntCtor
        {
            public HasIntCtor(int x) { }
        }

        public struct SomeStruct
        {
            public int v = 1;
        }

        public linear class Token
        {
        }

        public struct Pair
        {
            public int a = 1;
            public int b = 2;
        }

        public class WithBlock
        {
            public int a;
            public int b;

            default {
                a = 1
                b = 2
            }
        }

        public class Outer
        {
            public type Inner = new int;

            public Inner Make() { return (Inner)1; }
        }

        public struct Ordered
        {
            public int v = 1;

            public static bool operator ==(Ordered a, Ordered b) { return a.v == b.v; }
            public static bool operator !=(Ordered a, Ordered b) { return a.v != b.v; }
            public static bool operator <(Ordered a, Ordered b) { return a.v < b.v; }
            public static bool operator >(Ordered a, Ordered b) { return a.v > b.v; }
            public static bool operator <=(Ordered a, Ordered b) { return a.v <= b.v; }
            public static bool operator >=(Ordered a, Ordered b) { return a.v >= b.v; }
            public static Ordered operator *(int k, Ordered o) { return o; }
            public static Ordered operator -(Ordered o) { return o; }
        }

        public enum Tone : int { Low = 1, High = 2 }

        type Adult = int >= 18;
        type Senior = Adult >= 65;
        type Letter = char >= 'a' and <= 'z';
        type Ratio = double >= 0.0;
        type Warm = Tone >= Tone.High;
        type Yes = bool true;
        type NonEmpty = string not "";

        public readonly struct Tagged
        {
            public readonly string Name;
            public Tagged(string name) { Name = name; }
        }

        type Late = Tagged { Name: > "m" };

        public class Bounded
        {
            public int Small(byte b) where b < 10 { return 1; }
            public int Wide(ulong u) where u <= 100 { return 1; }
            public int Money(decimal d) where d >= 0 { return 1; }
            public int Half(double d) where d > 0.5 { return 1; }
            public int Lower(char c) where c >= 'a' { return 1; }
            public int Loud(Tone t) where t >= Tone.High { return 1; }
            public int Aged(Adult a) where a >= 30 { return 1; }
            public int Truth(bool b) where b true { return 1; }
            public int Text(string s) where s not "" { return 1; }
            public int this[int i] where i >= 0 { get { return i; } }
        }

        public class Ranged<K>
        {
            public int Same(K key) where key 18 { return 1; }
            public int Other(K key) where key not 18 { return 2; }
        }

        public class Sized<N:int> where N >= 0 { }
        public class Flagged<F:bool> where F true { }
        """;

    protected override string Preamble { get; } = """
        Owner owner = new Owner();
        Derived derived = new Derived();
        Final final = new Final();
        """;

    [Theory]
    [InlineData("int a = owner.open;")]
    [InlineData("int b = owner.shared;")]
    [InlineData("int c = derived.ReadGuarded();")]
    [InlineData("int d = owner.ReadOwn();")]
    [InlineData("Renamed r = final;")]
    [InlineData("int rm = new ReadonlyMethod().Get();")]
    [InlineData("var nested = new Outer().Make();")]
    [InlineData("Distinct e = Distinct.A;")]
    public void AnAccessInsideTheMembersDomain_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var r = new NeedsReadonly<int>();")]
    [InlineData("var f = new NeedsReadonly<Frozen>();")]
    [InlineData("var m = new NeedsMutable<Mutable>();")]
    [InlineData("var k = new NeedsCtor<HasIntCtor>();")]
    [InlineData("var any = new NeedsAny<Mutable>();")]
    [InlineData("var cl = new NeedsClass<Mutable>();")]
    [InlineData("var st = new NeedsStruct<SomeStruct>();")]
    [InlineData("var af = new NeedsAffine<Token>();")]
    [InlineData("var sh = new NeedsShape<Square>();")]
    [InlineData("var df = new NeedsDefault<Pair>();")]
    [InlineData("var di = new NeedsDefault<int>();")]
    public void AClosingThatSatisfiesItsConstraints_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("Pair p = default;")]
    [InlineData("WithBlock w = default;")]
    [InlineData("int i = default;")]
    [InlineData("int | void o = default;")]
    public void ADefaultOfATypeThatHasOne_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    /// <summary>
    /// Every pair complete, each over the same operands and result, and an operator whose declaring
    /// type is only its RIGHT operand -- found there, since an operator is searched on either operand.
    /// </summary>
    [Fact]
    public void ACompleteOperatorSetOnItsOwnType_IsAdmitted()
    {
        AssertSilent("var o = new Ordered();");
    }

    [Theory]
    [InlineData("Senior s = 70;")]
    [InlineData("Letter l = 'q';")]
    [InlineData("Warm w = Tone.High;")]
    [InlineData("Yes y = true;")]
    [InlineData("NonEmpty n = \"x\";")]
    [InlineData("int loud = new Bounded().Loud(Tone.High);")]
    [InlineData("int aged = new Bounded().Aged(40);")]
    [InlineData("var ranged = new Ranged<int>();")]
    [InlineData("var sized = new Sized<3>();")]
    [InlineData("var flagged = new Flagged<true>();")]
    public void ARefinementThatOrdersAnOrderedTypeOrOnlyComparesForEquality_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Fact]
    public void ARefinementOverATypeThatDidNotResolve_IsNotReportedAgain()
    {
        Assert.Empty(Analyse("int n = 1;", "public class Unresolved { public int Take(Nothing n) where n >= 3 { return 1; } }"));
    }

    [Theory]
    [InlineData("public class Sink { public static int Take(int a) { return 1; } public static int Take(int a, int b) { return 2; } }")]
    [InlineData("public class Sink { public static int Take(int a) { return 1; } public static int Give(int a) { return 2; } }")]
    [InlineData("public class Sink { public static int Take(ref int a) { return 1; } public static int Take(int a) { return 2; } }")]
    [InlineData("public class Sink { public static int Take<T>(int a) { return 1; } public static int Take(int a) { return 2; } }")]
    [InlineData("public class Sink { public static int Take<T, U>(T a, U b) { return 1; } public static int Take<T, U>(U a, T b) { return 2; } }")]
    [InlineData("public class Pair<A, B> { public int Take(A a) { return 1; } public int Take(B b) { return 2; } }")]
    [InlineData("public class Shadow<T> { public int Take<T>(T a) { return 1; } public int Take<U>(T a) { return 2; } }")]
    [InlineData("public type Meters = new int; public class Sink { public static int Take(Meters a) { return 1; } public static int Take(int a) { return 2; } }")]
    [InlineData("public class Sink { public static int Take(int? a) { return 1; } public static int Take(int a) { return 2; } }")]
    [InlineData("public class Left { public class Widget { } } public class Right { public class Widget { } } public class Sink { public static int Take(Left.Widget w) { return 1; } public static int Take(Right.Widget w) { return 2; } }")]
    [InlineData("public class Left { public class Widget { } } public class Right { public class Widget { } } public class Box<T> { } public class Sink { public static int Take(Box<Left.Widget> b) { return 1; } public static int Take(Box<Right.Widget> b) { return 2; } }")]
    [InlineData("public class Table { public int this[int i] { get { return 1; } } public int this[string s] { get { return 2; } } }")]
    [InlineData("public class Crate { public Crate(int a) { } public Crate(ref int a) { } }")]
    public void OverloadsWhoseSignaturesDiffer_AreNotDuplicates(string extraTypes)
    {
        AssertSilent("int unused = 1;", extraTypes);
    }

    [Fact]
    public void AGenericThatCollidesOnlyAfterSubstitution_IsNotADuplicate()
    {
        AssertSilent(
            "var keyed = new Keyed<int>();",
            "public class Keyed<K> { public int Take(K key) { return 1; } public int Take(int i) { return 2; } }");
    }

    [Theory]
    [InlineData("public class Root { public int Take(int a) { return 1; } } public class Leaf : Root { public int Take(string s) { return 2; } }")]
    [InlineData("abstract IBox<T> { T Wrap(T value); } public class IntBox : IBox<int> { public int Wrap(int value) { return value; } }")]
    [InlineData("abstract ISized { int Size { get; } } public class Boxed : ISized { public int Size { get { return 3; } } }")]
    [InlineData("public class Root { public int this[int i] { get { return 1; } } } public class Leaf : Root { public int this[string s] { get { return 2; } } }")]
    [InlineData("public class Root { public int Give(int a) { return 1; } } public class Leaf : Root { public int Take(int a) { return 2; } }")]
    public void AMemberThatDoesNotRepeatAnInheritedSignature_IsNotARedeclaration(string extraTypes)
    {
        AssertSilent("int unused = 1;", extraTypes);
    }

    [Theory]
    [InlineData("int z = nothing.secret;", "")]
    [InlineData("var q = new NeedsReadonly<Nothing>();", "")]
    [InlineData("int unused = 1;", "public class Sink { public static int Take(Nothing a) { return 1; } public static int Take(Nothing b) { return 2; } }")]
    [InlineData("int unused = 1;", "public class Box<T> { } public class Sink { public static int Take(Box<Nothing> a) { return 1; } public static int Take(Box<Nothing> b) { return 2; } }")]
    public void AUseThatAlreadyFailed_IsNotReportedAgain(string statement, string extraTypes)
    {
        AssertSilent(statement, extraTypes);
    }

    [Theory]
    [InlineData("int z = owner.secret;")]
    [InlineData("var q = new NeedsReadonly<Mutable>();")]
    public void AViolationIsReportedOnce(string statement)
    {
        _ = Assert.Single(Analyse(statement));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertSilent(string statement, string extraTypes = "")
    {
        Assert.Empty(Analyse(statement, extraTypes));
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes = "")
    {
        var model = BuildModel(statement, extraTypes, extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}