// Tier 3 (Component): what order 6's rules deliberately do NOT report, and the reach facts the corpus
// cannot phrase. The fixture types are themselves under analysis in every row, so the accesses their
// bodies make -- a private member from its own type and a nested one, a protected member from a
// derivation, an internal member across types of one compilation, a public class over an internal
// abstract -- are silences proven by every row. WHY each is silent is recorded once, on order 6 of
// backlog/analysis-v2-capability-progression.yaml.

using System.Collections.Immutable;
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
        "MIST_PA_OPERATOR_NOT_ON_DECLARING_TYPE", "MIST_PA_REFINEMENT_NOT_COMPARABLE",
        "MIST_PA_DUPLICATE_SIGNATURE", "MIST_PA_MEMBER_REDECLARATION", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER",
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

    [Fact]
    public void AMemberNamedByAnInitializerAPatternOrAnIndexInsideItsDomain_IsAdmitted()
    {
        const string keeper = """
            public class Keeper
            {
                private int hidden;
                private int Tucked { get; set; }
                protected int Guarded { get; set; }

                private int this[int slot]
                {
                    get { return slot; }
                }

                public static Keeper Make() { return new Keeper { hidden = 1, Tucked = 2, Guarded = 3 }; }

                public bool Check() { return this is { hidden: 1, Tucked: 2 } && this[0] == 0; }

                public class Inner
                {
                    public static bool Peek(Keeper k) { return k is { hidden: 1, Tucked: 2 } && k[1] == 1; }
                }
            }

            public class Heir : Keeper
            {
                public static Heir Build() { return new Heir { Guarded = 4 }; }

                public bool Guards() { return this is { Guarded: 4 }; }
            }
            """;

        AssertBoundAndSilent("bool ok = Keeper.Make().Check() && Keeper.Inner.Peek(Keeper.Make()) && Heir.Build().Guards();", keeper);
    }

    [Theory]
    [InlineData("""
        public class Gauge
        {
            private readonly int level = 1;

            private type Low = Gauge ({ level: < 3 });

            private static int Take(Gauge gauge) where gauge { level: < 3 }
            {
                return 1;
            }

            public bool IsLow() { return this is Low; }
        }
        """)]
    [InlineData("""
        public class Gauge
        {
            private readonly int level = 1;

            private class Inner
            {
                public type Low = Gauge ({ level: < 3 });

                public static bool Test(Gauge gauge) { return gauge is Low; }
            }

            public bool IsLow() { return Inner.Test(this); }
        }
        """)]
    [InlineData("""
        public class Gauge
        {
            protected readonly int level = 1;

            internal readonly int shared = 2;
        }

        public class Wider : Gauge
        {
            protected type Low = Gauge ({ level: < 3 });
        }

        internal type Shared = Gauge ({ shared: < 3 });
        """)]
    public void ARefinementNoMoreAccessibleThanTheMembersItReads_IsDeclared(string extraTypes)
    {
        AssertBoundAndSilent("int unused = 1;", extraTypes);
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
    [InlineData("public class Crock { public int Size { get; } } type Petite = Crock { Size: < 3 };")]
    [InlineData("public class Crock { public readonly int Size = 1; } type Petite = Crock { Size: < 3 };")]
    [InlineData("public class Cork { public int Width { get; } } public class Crock { public Cork Top { get; } public int Size { get; set; } } type Petite = Crock { Top: { Width: < 3 } };")]
    [InlineData("public class Crock { public int Size { get; set; } } type Petite = Crock; type Tiny = int < 3; public class Sink { public static int Take(Crock crock, int size) where size < 3 { crock.Size = size; return size; } }")]
    [InlineData("public class Crock { public int Size { get; init; } } type Petite = Crock { Size: < 3 };")]
    [InlineData("public class Crock { readonly int size = 1; public int Size { get { return size; } init { } } } type Petite = Crock { Size: < 3 };")]
    [InlineData("public class Crock { readonly int size = 1; public int Size { get { return size; } } public int Twice { get { return Size + this.size; } } } type Petite = Crock { Twice: < 6 };")]
    [InlineData("public class Crock { readonly int size = 1; public int Size => size; } type Petite = Crock { Size: < 3 };")]
    [InlineData("public class Cork { public int Width { get; } } public class Crock { readonly Cork top = new Cork(); public int Size { get { return top.Width; } } } type Petite = Crock { Size: < 3 };")]
    [InlineData("public class Crock { readonly int[3] cells = new int[3]; public int Size { get { return cells.Length; } } } type Petite = Crock { Size: < 9 };")]
    [InlineData("public class Crock { readonly int size = 1; public bool Big { get { if(size > 2) { return true; } return false; } } } type Petite = Crock { Big: false };")]
    [InlineData("intrinsic public readonly class Word { public int Length { get; } } type Long = Word { Length: > 3 };")]
    public void APredicateOverMembersNothingCanWrite_IsDeclared(string extraTypes)
    {
        AssertSilent("int unused = 1;", extraTypes);
    }

    [Theory]
    [InlineData("int z = nothing.secret;", "")]
    [InlineData("var q = new NeedsReadonly<Nothing>();", "")]
    [InlineData("int unused = 1;", "public class Sink { public static int Take(Nothing a) { return 1; } public static int Take(Nothing b) { return 2; } }")]
    [InlineData("int unused = 1;", "public class Box<T> { } public class Sink { public static int Take(Box<Nothing> a) { return 1; } public static int Take(Box<Nothing> b) { return 2; } }")]
    [InlineData("var kin = new Kin<Nothing, Dog>();", Closings)]
    [InlineData("var link = new Link<Nothing, Badge>();", Closings)]
    [InlineData("var tie = Ranks.Tie<Nothing, Badge>(1, new Badge());", Closings)]
    [InlineData("var made = new Factory<Widget, Nothing>();", Closings)]
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

    [Theory]
    [InlineData("func<Square, int> named = Shapes.Sides;", 0)]
    [InlineData("func<Mutable, int> named = Shapes.Sides;", 1)]
    public void ATypeArgumentAMethodGroupInfers_IsHeldToTheMethodsConstraint(string statement, int findings)
    {
        const string shapes = "public class Shapes { public static int Sides<T>(T shape) where T : IShape { return 4; } }";

        Assert.Equal(findings, Analyse(statement, shapes).Count(d => d.Id.Value == "MIST_PA_CONSTRAINT_NOT_SATISFIED"));
    }

    private const string Closings = """
        abstract IRank<T> { int CompareTo(T other); }
        public class Score : IRank<Score> { public int CompareTo(Score other) { return 0; } }
        public class Grade : IRank<Score> { public int CompareTo(Score other) { return 0; } }
        public class Animal { }
        public class Dog : Animal { }
        abstract IKeyed<K> { K Key(); }
        public class Badge : IKeyed<string> { public string Key() { return "b"; } }
        public class Widget { public Widget(int size) { } }
        public class Sorted<T> where T : IRank<T> { }
        public class Kin<T, U> where U : T { }
        public class Link<K, V> where V : IKeyed<K> { }
        public class Factory<T, A> where T : new(A) { }
        public class Row<params Ts> where Ts : IRank<Ts> { }
        public class Keys<K, params Vs> where Vs : IKeyed<K> { }
        public class Shelf<T> { public int Put<U>(U item) where U : T { return 1; } }
        public class Rack : Shelf<Animal> { }
        public class Tray<T> { }
        <T> extension(Tray<T> self) { public int Put<U>(U item) where U : T { return 1; } }
        public class Ranks
        {
            public static int Pick<T>(T value) where T : IRank<T> { return 2; }
            public static int Tie<K, V>(K key, V value) where V : IKeyed<K> { return 3; }
        }
        """;

    [Theory]
    [InlineData("var sorted = new Sorted<Score>();", "")]
    [InlineData("Sorted<Score> held = new Sorted<Score>();", "")]
    [InlineData("int unused = 1;", "public class Board : Sorted<Score> { }")]
    [InlineData("var kin = new Kin<Animal, Dog>();", "")]
    [InlineData("var link = new Link<string, Badge>();", "")]
    [InlineData("var made = new Factory<Widget, int>();", "")]
    [InlineData("var row = new Row<Score, Score>();", "")]
    [InlineData("var keys = new Keys<string, Badge, Badge>();", "")]
    [InlineData("var picked = Ranks.Pick<Score>(new Score());", "")]
    [InlineData("var picked = Ranks.Pick(new Score());", "")]
    [InlineData("func<Score, int> picked = Ranks.Pick;", "")]
    [InlineData("var tied = Ranks.Tie<string, Badge>(\"k\", new Badge());", "")]
    [InlineData("var tied = Ranks.Tie(\"k\", new Badge());", "")]
    [InlineData("var put = new Shelf<Animal>().Put<Dog>(new Dog());", "")]
    [InlineData("var put = new Shelf<Animal>().Put(new Dog());", "")]
    [InlineData("var put = new Rack().Put(new Dog());", "")]
    [InlineData("func<Dog, int> put = new Shelf<Animal>().Put;", "")]
    [InlineData("var put = new Tray<Animal>().Put<Dog>(new Dog());", "")]
    [InlineData("var put = new Tray<Animal>().Put(new Dog());", "")]
    public void AConstraintNamingAParameterIsJudgedAsTheClosingFillsIt_AndAdmitsTheRightArgument(string statement, string extraTypes)
    {
        AssertBoundAndSilent(statement, Closings + extraTypes);
    }

    [Theory]
    [InlineData("var sorted = new Sorted<Grade>();", "", "'IRank<Grade>' constraint on type parameter 'T'")]
    [InlineData("int unused = 1;", "public class Board : Sorted<Grade> { }", "'IRank<Grade>' constraint on type parameter 'T'")]
    [InlineData("var kin = new Kin<Dog, Animal>();", "", "'Dog' constraint on type parameter 'U'")]
    [InlineData("var link = new Link<int, Badge>();", "", "'IKeyed<int>' constraint on type parameter 'V'")]
    [InlineData("var made = new Factory<Widget, string>();", "", "'new(string)' constraint on type parameter 'T'")]
    [InlineData("var row = new Row<Score, Grade>();", "", "'IRank<Grade>' constraint on type parameter 'Ts'")]
    [InlineData("var keys = new Keys<int, Badge>();", "", "'IKeyed<int>' constraint on type parameter 'Vs'")]
    [InlineData("var picked = Ranks.Pick<Grade>(new Grade());", "", "'IRank<Grade>' constraint on type parameter 'T'")]
    [InlineData("func<Grade, int> picked = Ranks.Pick;", "", "'IRank<Grade>' constraint on type parameter 'T'")]
    [InlineData("var tied = Ranks.Tie<int, Badge>(1, new Badge());", "", "'IKeyed<int>' constraint on type parameter 'V'")]
    [InlineData("var put = new Shelf<Dog>().Put<Animal>(new Animal());", "", "'Dog' constraint on type parameter 'U'")]
    [InlineData("var put = new Rack().Put(new Score());", "", "'Animal' constraint on type parameter 'U'")]
    [InlineData("func<Animal, int> put = new Shelf<Dog>().Put;", "", "'Dog' constraint on type parameter 'U'")]
    [InlineData("var put = new Tray<Dog>().Put<Animal>(new Animal());", "", "'Dog' constraint on type parameter 'U'")]
    public void AConstraintNamingAParameterIsJudgedAsTheClosingFillsIt_AndRefusesTheWrongArgument(
        string statement, string extraTypes, string constraint)
    {
        var finding = Assert.Single(Analyse(statement, Closings + extraTypes));

        Assert.Contains(constraint, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var picked = Ranks.Pick<Grade>(new Grade());", "var picked = Ranks.Pick(new Grade());")]
    [InlineData("var tied = Ranks.Tie<int, Badge>(1, new Badge());", "var tied = Ranks.Tie(1, new Badge());")]
    [InlineData("var put = new Shelf<Dog>().Put<Animal>(new Animal());", "var put = new Shelf<Dog>().Put(new Animal());")]
    [InlineData("var put = new Tray<Dog>().Put<Animal>(new Animal());", "var put = new Tray<Dog>().Put(new Animal());")]
    public void AnInferredMethodClosing_IsJudgedAsTheSameClosingWrittenOut(string written, string inferred)
    {
        var expected = Analyse(written, Closings).Select(d => d.Message).ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, Analyse(inferred, Closings).Select(d => d.Message));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertSilent(string statement, string extraTypes = "")
    {
        Assert.Empty(Analyse(statement, extraTypes));
    }

    private void AssertBoundAndSilent(string statement, string extraTypes)
    {
        _ = BuildModel("int unused = 1;", extraTypes: "", extraPreamble: "", out var fixtures);
        _ = BuildModel(statement, extraTypes, extraPreamble: "", out var build);

        Assert.Equal(ErrorMessages(fixtures), ErrorMessages(build));
        AssertSilent(statement, extraTypes);
    }

    private static List<string> ErrorMessages(ImmutableArray<Diagnostic> diagnostics)
    {
        return [.. diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)];
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes = "")
    {
        var model = BuildModel(statement, extraTypes, extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}