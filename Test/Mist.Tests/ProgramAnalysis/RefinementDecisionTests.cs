// Tier 3 (Component): what order 12's prover deliberately does NOT report -- the constants that
// satisfy, the implications that hold, the narrowings the flow establishes, the indices it proves,
// and the arguments a call discharges the same way -- and the reach facts the corpus cannot phrase.
// WHY each is silent, and which of them classic refuses, is recorded once, on order 12 of
// backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinementDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_REFINEMENT_VIOLATED", "MIST_PA_REFINEMENT_UNPROVEN", "MIST_PA_REFINEMENT_NOT_IMPLIED",
        "MIST_PA_INDEX_OUT_OF_BOUNDS", "MIST_PA_INDEX_UNPROVEN", "MIST_PA_CONSTRAINT_VIOLATED",
        "MIST_PA_CONSTRAINT_UNPROVEN", "MIST_PA_DEPENDENT_ARGUMENT_VIOLATED", "MIST_PA_DEPENDENT_FORWARD_UNPROVEN",
        "MIST_PA_CONSTRAINT_STRENGTHENED",
    ];

    public RefinementDecisionTests()
        : base("MistRefinement_")
    {
    }

    protected override string Fixtures { get; } = """
        type AdultAge = int >= 18;
        type DrinkingAge = int >= 21;
        type Percentage = int >= 0 and <= 100;
        type Digit = int >= 0 and <= 9;
        type Even = int (2 or 4 or 6);
        type NotZero = int not 0;
        type Upper = char >= 'A' and <= 'Z';
        type Flag = bool true;
        type Small = byte <= 10;
        type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
        type Named = string "x";
        type AgeOrName = AdultAge | string;
        type SmallOrName = Small | string;

        public class Buffer
        {
            int[5] items = new int[5];

            public int this[int index] where index >= 0 and < 5
            {
                get { return items[index]; }
                set { items[index] = value; }
            }
        }

        public class Ledger
        {
            int[3] entries = new int[3];

            public int Count { get { return entries.Length; } }

            public int this[int entry] where entry >= 0 and < 3
            {
                get { return entries[entry]; }
            }
        }

        public class FixedArray<TValue, Size:int> where Size >= 0
        {
            public int Count()
            {
                return Size;
            }
        }

        public class Wrapper<N:int> : FixedArray<string, N> where N >= 0
        {
        }

        public abstract IValidator
        {
            string Validate(int value) where value >= 0;
        }

        public class Same : IValidator
        {
            public string Validate(int value) where value >= 0
            {
                return "same";
            }
        }

        public class Lax : IValidator
        {
            public string Validate(int value) where value >= -5
            {
                return "lax";
            }
        }

        public class Lenient : IValidator
        {
            public string Validate(int value)
            {
                return "lenient";
            }
        }

        public class Bounded
        {
            public int Size;

            public Bounded(int size) where size > 0
            {
                Size = size;
            }

            public Bounded() : this(1)
            {
            }
        }

        public class Derived : Bounded
        {
            public Derived() : base(3)
            {
            }
        }

        public class Person
        {
            public AdultAge Age;

            public Person(AdultAge age)
            {
                Age = age;
            }
        }

        public class Voter
        {
            public int Age;

            public Voter(int age)
            {
                Age = age;
            }

            public static explicit operator Voter(AdultAge age)
            {
                return new Voter(age);
            }
        }

        public class Keyed<K>
        {
            public int Ne(K key) where key not 18 { return 2; }
        }

        public class Any
        {
            public static int Ne<T>(T key) where key not 18 { return 2; }
        }

        public class Ordered<K>
        {
            public int Ge(K key) where key >= 18 { return 2; }
        }

        public class Helpers
        {
            public static int Twice(AdultAge a) { return a + a; }
            public static string Older(DrinkingAge d) { return "ok"; }
            public static string Process(int x) where x < 100 { return "ok"; }
            public static int Forward(int x) where x >= 0 and < 50 { return Helpers.Process(x).Length; }
            public static int Both(AdultAge a) where a <= 65 { return a; }
            public static int Rebound(int x) where x > 90 { x = 95; return x; }
            public static string Pick(int x) where x < 100 { return "int"; }
            public static string Pick(string s) { return s; }
            public static string Choose(int x) where x < 50 { Console.WriteLine(x); return Helpers.Pick(x); }
            public static AdultAge Grown() { return 30; }
            public static int Sum(int[] items) { int s = 0; for(int i = 0; i < items.Length; i = i + 1) { s = s + items[i]; } return s; }
            public static int Sized(int[] items, int i) { if(i >= 0 && i < items.Length) { return items[i]; } return 0; }
            public static int Narrowed(int x) where x >= 0 { Percentage p = 0; if(x <= 100) { p = x; } return p; }
        }
        """;

    protected override string Preamble { get; } = """
        var flag = true;
        int x = 20;
        AdultAge adult = 30;
        DrinkingAge drinking = 40;
        Digit digit = 5;
        int[] items = [1, 2, 3];
        """;

    [Theory]
    [InlineData("AdultAge a = 18;")]
    [InlineData("AdultAge a = 65;")]
    [InlineData("Percentage p = 0; Percentage q = 100;")]
    [InlineData("Even e = 4;")]
    [InlineData("NotZero n = -3;")]
    [InlineData("Upper u = 'Q';")]
    [InlineData("Flag f = true;")]
    [InlineData("Small s = 10;")]
    [InlineData("AgeRange r = 5; AgeRange b = 50;")]
    [InlineData("AdultAge a = adult;")]
    [InlineData("AdultAge a = drinking;")]
    [InlineData("Percentage p = digit;")]
    [InlineData("int i = adult;")]
    [InlineData("AdultAge a = Helpers.Grown();")]
    [InlineData("int y = 40; AdultAge a = y;")]
    public void AValueThatSatisfiesTheRefinement_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("if(x >= 18) { AdultAge a = x; }")]
    [InlineData("if(x > 17) { AdultAge a = x; }")]
    [InlineData("if(x >= 21) { AdultAge a = x; }")]
    [InlineData("if(18 <= x) { AdultAge a = x; }")]
    [InlineData("if(x is >= 18) { AdultAge a = x; }")]
    [InlineData("if(x is AdultAge a) { AdultAge b = x; DrinkingAge c = 21; }")]
    [InlineData("if(x < 18) { return; } AdultAge a = x;")]
    [InlineData("if(x >= 0 && x <= 100) { Percentage p = x; }")]
    [InlineData("if(x >= 0) { if(x <= 100) { Percentage p = x; } }")]
    [InlineData("if(x is >= 0 and <= 100) { Percentage p = x; }")]
    [InlineData("if(x >= 18 || x >= 30) { AdultAge a = x; }")]
    [InlineData("while(x >= 18) { AdultAge a = x; x = x - 1; }")]
    [InlineData("if(!(x < 18)) { AdultAge a = x; }")]
    public void ANarrowingTheFlowEstablishes_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var a = new int[3]; Console.WriteLine(a[0] + a[2]);")]
    [InlineData("var a = new int[3]; for(var i = 0; i < 3; i = i + 1) { Console.WriteLine(a[i]); }")]
    [InlineData("var a = new int[3]; for(var i = 0; i < a.Length; i = i + 1) { Console.WriteLine(a[i]); }")]
    [InlineData("var a = new int[3]; for(var i = 2; i >= 0; i = i - 1) { Console.WriteLine(a[i]); }")]
    [InlineData("Console.WriteLine(items[0]);")]
    [InlineData("Console.WriteLine(Helpers.Sum(items) + Helpers.Sized(items, 1));")]
    [InlineData("var a = new int[10]; Console.WriteLine(a[digit]);")]
    [InlineData("var b = new Buffer(); Console.WriteLine(b[4]); b[0] = 1;")]
    [InlineData("var b = new Buffer(); if(x >= 0 && x < 5) { Console.WriteLine(b[x]); }")]
    [InlineData("Console.WriteLine(items[^1]);")]
    [InlineData("var a = new int[3]; Console.WriteLine(a[^1] + a[^3]);")]
    [InlineData("var a = new int[3]; a[^1] = 1;")]
    [InlineData("var a = new int[3]; var k = 2; Console.WriteLine(a[^k]);")]
    [InlineData("var l = new Ledger(); Console.WriteLine(l[^1]);")]
    [InlineData("Console.WriteLine(items[2]);")]
    [InlineData("int[] a = new int[3]; a[2] = 1; Console.WriteLine(a[0]);")]
    [InlineData("Standard.Span<int> b = stackalloc int[4]; b[3] = 1;")]
    [InlineData("var a = [1, 2, 3, 4, 5]; var m = a[1..3]; Console.WriteLine(m[1]);")]
    [InlineData("var a = [1, 2, 3, 4, 5]; if(a is [var f, .. var mid, var l]) { Console.WriteLine(mid[2]); }")]
    [InlineData("Standard.Span<int> s = stackalloc int[4]; var t = s.Slice(1, 2); Console.WriteLine(t[1]);")]
    [InlineData("var a = new int[3]; if(x >= 0) { if(x < 3) { Console.WriteLine(a[x]); } }")]
    [InlineData("var d = [[1, 2], [3, 4]]; for(int i = 0; i < d.Length; i = i + 1) { Console.WriteLine(d[i][0]); }")]
    [InlineData("var d = [[1, 2], [3, 4]]; int s = 0; for(int i = 0; i < d.Length; i = i + 1) { for(int j = 0; j < d[i].Length; j = j + 1) { s = s + d[i][j]; } }")]
    [InlineData("var d = [1, 2]; for(int i = 0; i < d.Length; i = i + 1) { Console.WriteLine(d[i]); }")]
    public void AnIndexTheFlowProves_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("int[] items = new int[3]; public int Run() { int s = 0; for(int i = 0; i < items.Length; i = i + 1) { s = s + items[i]; } return s; }")]
    [InlineData("int[] items = new int[3]; public int Run() { int s = 0; for(int i = 0; i < this.items.Length; i = i + 1) { s = s + this.items[i]; } return s; }")]
    [InlineData("int[] items = new int[3]; public int Run(int i) { if(i >= 0 && i < items.Length) { return items[i]; } return 0; }")]
    [InlineData("int[] items = new int[3]; public int Run() { int s = 0; for(int i = 0; i < items.Length; i = i + 1) { s = s + items[i]; Log(); } return s; } static void Log() { }")]
    [InlineData("readonly int[] items = new int[3]; public int Run() { int s = 0; for(int i = 0; i < items.Length; i = i + 1) { Log(); s = s + items[i]; } return s; } static void Log() { }")]
    [InlineData("readonly int[] items = new int[3]; public int Run(int i) { if(i >= 0 && i < items.Length) { var other = new Held(); return items[i]; } return 0; }")]
    public void AnIndexIntoAFieldHeldArrayTheFlowProves_IsAdmitted(string members)
    {
        AssertSilent("var held = new Held();", $"public class Held {{ {members} }}");
    }

    [Theory]
    [InlineData("public static int First(int[] xs) { if(xs.Length > 0) { return xs[0]; } return 0; }")]
    [InlineData("public static int Last(int[] xs) { if(xs.Length > 0) { return xs[^1]; } return 0; }")]
    [InlineData("public static int Second(int[] xs) { if(xs.Length < 2) { return 0; } return xs[1]; }")]
    [InlineData("public static int Pair(int[] xs) { if(2 <= xs.Length) { return xs[0] + xs[1]; } return 0; }")]
    [InlineData("public static int Tail(int[] xs, int k) { if(k >= 1 && k <= 2 && xs.Length >= 2) { return xs[^k]; } return 0; }")]
    [InlineData("int[] cells = new int[2]; public int First() { if(cells.Length > 0) { return cells[0]; } return 0; }")]
    [InlineData("int[] cells = new int[2]; public int Second() { if(this.cells.Length > 1) { return this.cells[1]; } return 0; }")]
    [InlineData("int[] cells = new int[2]; public int Last() { if(cells.Length >= 2) { return cells[^2]; } return 0; }")]
    [InlineData("readonly int[] cells = new int[2]; public int Kept() { if(cells.Length > 1) { Held.Log(); return cells[1]; } return 0; } static void Log() { }")]
    [InlineData("int[] cells = new int[2]; public int Fresh() { cells = new int[3]; return cells[2]; }")]
    [InlineData("int[] cells = new int[2]; public int Plain { get; set; } public int First() { if(cells.Length > 0) { var p = Plain; Plain = 2; return cells[0]; } return 0; }")]
    public void AnIndexALengthGuardProves_IsAdmitted(string members)
    {
        AssertSilent("var held = new Held();", $"public class Held {{ {members} }}");
    }

    [Theory]
    [InlineData("var a = new int[3]; a[0] = 1; a[2] = a[0];")]
    [InlineData("var a = new int[3]; for(var i = 0; i < a.Length; i = i + 1) { a[i] = i; }")]
    [InlineData("if(x >= 0 && x < items.Length) { items[x] = 1; }")]
    [InlineData("var b = new Buffer(); b[4] = 1;")]
    public void AnIndexWriteTheFlowProves_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("int[] items = new int[3]; public void Run() { for(int i = 0; i < items.Length; i = i + 1) { items[i] = i; } }")]
    [InlineData("int[] items = new int[3]; public void Run(int i) { if(i >= 0 && i < this.items.Length) { this.items[i] = 1; } }")]
    public void AnIndexWriteIntoAFieldHeldArrayTheFlowProves_IsAdmitted(string members)
    {
        AssertSilent("var held = new Held();", $"public class Held {{ {members} }}");
    }

    [Theory]
    [InlineData("Console.WriteLine(Helpers.Twice(30));")]
    [InlineData("Console.WriteLine(Helpers.Twice(adult) + Helpers.Twice(drinking));")]
    [InlineData("int y = 40; Console.WriteLine(Helpers.Twice(y));")]
    [InlineData("if(x >= 18) { Console.WriteLine(Helpers.Twice(x)); }")]
    [InlineData("if(x is AdultAge a) { Console.WriteLine(Helpers.Twice(x)); }")]
    [InlineData("Console.WriteLine(Helpers.Older(drinking));")]
    [InlineData("Console.WriteLine(Helpers.Process(42) + Helpers.Process(digit));")]
    [InlineData("if(x < 100) { Console.WriteLine(Helpers.Process(x)); }")]
    [InlineData("Console.WriteLine(Helpers.Narrowed(5) + Helpers.Forward(7));")]
    [InlineData("Console.WriteLine(Helpers.Both(30) + Helpers.Both(drinking));")]
    [InlineData("var b = new Bounded(3); Console.WriteLine(b.Size);")]
    [InlineData("var d = new Derived(); var b = new Bounded(); Console.WriteLine(d.Size + b.Size);")]
    [InlineData("Console.WriteLine(Helpers.Rebound(100));")]
    [InlineData("Console.WriteLine(Helpers.Pick(42) + Helpers.Pick(\"a\") + Helpers.Choose(7));")]
    [InlineData("Console.WriteLine(adult); Console.WriteLine(Helpers.Pick(adult));")]
    [InlineData("var p = new Person(adult); Console.WriteLine(p.Age);")]
    [InlineData("IValidator v = new Same(); Console.WriteLine(v.Validate(0));")]
    [InlineData("var f = new FixedArray<int, 3>(); var w = new Wrapper<2>(); Console.WriteLine(f.Count() + w.Count());")]
    public void AnArgumentTheProverDischarges_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var keyed = new Keyed<int>(); Console.WriteLine(keyed.Ne(3));")]
    [InlineData("var keyed = new Keyed<int>(); int y = 3; Console.WriteLine(keyed.Ne(y));")]
    [InlineData("var keyed = new Keyed<int>(); if(x != 18) { Console.WriteLine(keyed.Ne(x)); }")]
    [InlineData("var keyed = new Keyed<long>(); Console.WriteLine(keyed.Ne(3L));")]
    [InlineData("Console.WriteLine(Any.Ne(3));")]
    [InlineData("Console.WriteLine(Any.Ne<int>(3));")]
    public void AWhereOverATypeParameterTheReceiverClosed_IsProvenAgainstTheArgument(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var ordered = new Ordered<int>(); Console.WriteLine(ordered.Ge(3));")]
    [InlineData("var ordered = new Ordered<string>(); Console.WriteLine(ordered.Ge(\"x\"));")]
    public void AWhereRefusedAtItsDeclaration_IsNotProvenAgainAtTheClosedReceiver(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var a = (AdultAge)21;")]
    [InlineData("if(x >= 18) { var a = (AdultAge)x; }")]
    [InlineData("int i = (int)adult;")]
    [InlineData("var a = (AdultAge)drinking;")]
    [InlineData("var s = (Small)10;")]
    [InlineData("int y = 40; var a = (AdultAge)y;")]
    [InlineData("var u = (AgeOrName)adult; var v = (SmallOrName)10;")]
    [InlineData("var v = (Voter)25; var w = (Voter)drinking; if(x >= 18) { var z = (Voter)x; }")]
    public void ACastTheProverDischarges_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("AdultAge a = Lifts.Twenty();", "public static int Twenty() { return 20; }")]
    [InlineData("Percentage p = Lifts.Pick(50);", "public static int Pick(Percentage p) { return p; }")]
    [InlineData("AdultAge a = Lifts.Either(flag);", "public static int Either(bool b) { if(b) { return 18; } return 30; }")]
    [InlineData("AdultAge a = Lifts.Outer();", "public static int Inner() { return 19; } public static int Outer() { return Lifts.Inner(); }")]
    [InlineData("AdultAge a = Lifts.Returned();", "public static int Returned() { var g = () => 5; return 21; }")]
    [InlineData("var l = new Lifts(); AdultAge a = l.Age;", "public int Age { get { return 40; } }")]
    [InlineData("var l = new Lifts(); AdultAge a = l[0];", "public int this[int i] { get { return 25; } }")]
    [InlineData("var l = new Lifts(); var a = new int[4]; var v = a[l.Three];", "int[3] cells = new int[3]; public int Three { get { return cells.Length; } }")]
    public void AValueAMembersReturnsLift_IsAdmitted(string statement, string members)
    {
        AssertSilent(statement, $"public class Lifts {{ {members} }}");
    }

    [Theory]
    [InlineData("var s = new Counted(); int v = s[^1];", "public int Length { get { return 5; } } public int Count { get { return 9; } } public int this[int slot] { get { return slot; } }")]
    [InlineData("var s = new Counted(); int v = s[^3];", "int[3] cells = new int[3]; public int Count { get { return cells.Length; } } public int this[int e] where e >= 0 and < 3 { get { return e; } }")]
    [InlineData("var s = new Counted(); if(s is [.., var y]) { int v = y; }", "int[3] cells = new int[3]; public int Count { get { return cells.Length; } } public int this[int e] where e >= 0 and < 3 { get { return e; } }")]
    [InlineData("var s = new Counted(); if(s is [var a, var b, var c]) { int v = a + b + c; }", "int count = 9; public int Count { get { return count; } } public int this[int e] where e >= 0 and < 3 { get { return e; } }")]
    [InlineData("var s = new Counted(); if(s is [_, _, _, _, _, _, _, _, _]) { int v = 1; }", "int count = 9; public int Count { get { return count; } } public int this[int e] where e >= 0 and < 3 { get { return e; } }")]
    [InlineData("var s = new Counted(); if(s is [var a, ..]) { int v = a; }", "int count = 9; public int Count { get { return count; } } public int this[int e] where e >= 0 and < 3 { get { return e; } }")]
    public void APositionTheCountProves_IsAdmitted(string statement, string members)
    {
        AssertSilent(statement, $"public class Counted {{ {members} }}");
    }

    [Theory]
    [InlineData("Senior s = 70;", "type Adult = int >= 18; type Senior = Adult and >= 65;")]
    [InlineData("int Half(int n) where n >= 0 and <= 10 { return n / 2; } var h = Half(8);", "")]
    [InlineData("var crock = new Crock(); int v = crock.Wide(5);", "public class Crock { } extension(Crock self) { public int Wide(int other) where other >= 3 { return other; } }")]
    [InlineData("var crock = new Crock(); int v = crock.Wide(x) + crock.Wide(adult);", "public class Crock { } extension(Crock self) { public int Wide(int other) where other >= 3 { return other; } }")]
    [InlineData("var keyed = new Keyed<int>(); int v = keyed.Echo(3) + keyed.Echo(x);", "<K> extension(Keyed<K> self) { public K Echo(K key) where key not 18 { return key; } }")]
    [InlineData("var crock = new Crock(); int v = crock.Sized<5>();", "public class Crock { } extension(Crock self) { public int Sized<N:int>() where N >= 3 { return N; } }")]
    public void APredicateTheBinderUsedToDrop_IsProvenWhereItHolds(string statement, string extraTypes)
    {
        AssertSilent(statement, extraTypes);
    }

    [Theory]
    [InlineData("var box = new Box(1); if(box is Petite s) { Petite t = s; }", "public class Box { public Box(int size) { Size = size; } public int Size { get; } } type Petite = Box { Size: < 3 };")]
    [InlineData("var k = 1;", "public class Box { public Box(int size) { Size = size; } public int Size { get; } } type Petite = Box { Size: < 3 }; public class Keeper { public static Petite Keep(Petite s) { return s; } public static string Pass(Petite s) { return Keeper.Name(s); } public static string Name(Petite s) { return \"small\"; } }")]
    [InlineData("Code c = 5;", "type Code = int >= 0 | string \"x\";")]
    public void AnUnreadablePredicate_IsMetByAValueOfItsOwnType(string statement, string extraTypes)
    {
        AssertSilent(statement, extraTypes);
    }

    [Theory]
    [InlineData("var l = new Tally(); int v = l[1] + l[^1];", "public class Tally { public int Count { get { return 3; } } public int this[int e] where e >= 0 and < Count { get { return e; } } public int Take(int n) where n >= 0 and < this.Count { return n; } }")]
    [InlineData("var l = new Tally(); if(l is [var a, var b, var c]) { int v = a; }", "public class Tally { public int Count { get { return 3; } } public int this[int e] where e >= 0 and < Count { get { return e; } } public int Take(int n) where n >= 0 and < this.Count { return n; } }")]
    [InlineData("var l = new Tally(); int v = l.Take(2);", "public class Tally { public int Count { get { return 3; } } public int this[int e] where e >= 0 and < Count { get { return e; } } public int Take(int n) where n >= 0 and < this.Count { return n; } }")]
    [InlineData("var l = new Tally(); int v = l[2];", "public class Tally { bool small = true; public int Count { get { if(small) { return 3; } return 5; } } public int this[int e] where e >= 0 and < Count { get { return e; } } }")]
    public void AMemberBoundReadThroughItsSignature_IsAdmitted(string statement, string extraTypes)
    {
        AssertSilent(statement, extraTypes);
    }

    [Theory]
    [InlineData("AdultAge a = 7; AdultAge b = 7;")]
    [InlineData("var a = new int[3]; Console.WriteLine(a[5] + a[5]);")]
    [InlineData("Console.WriteLine(Helpers.Twice(3) + Helpers.Twice(3));")]
    [InlineData("AdultAge a = (AdultAge)7; AdultAge b = (AdultAge)7;")]
    public void AFindingIsReportedPerSite(string statement)
    {
        Assert.Equal(2, Analyse(statement).Count);
    }

    [Theory]
    [InlineData("AdultAge a = nothing;")]
    [InlineData("var a = new int[3]; Console.WriteLine(a[nothing]);")]
    [InlineData("Console.WriteLine(Helpers.Twice(nothing));")]
    [InlineData("var a = (AdultAge)nothing;")]
    public void AValueThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("Upper u = 5;", "")]
    [InlineData("AdultAge a = 5L;", "")]
    [InlineData("Small s = 10; s = 300;", "")]
    [InlineData("SmallOrName n = 300;", "")]
    [InlineData("int r = Taker.Take(300);", "public class Taker { public static int Take(Small s) { return s; } }")]
    [InlineData("int r = Taker.Take(300);", "public class Taker { public static int Take(byte b) where b <= 10 { return b; } }")]
    [InlineData("int r = Taker.Take(x);", "public class Taker { public static int Take(byte b) where b <= 10 { return b; } }")]
    [InlineData("int k = Keeper.Reset(1);", "public class Keeper { public static int Reset(byte b) where b <= 10 { b = 300; return b; } }")]
    [InlineData("Small g = Giver.Give();", "public class Giver { public static Small Give() { return 300; } }")]
    [InlineData("var c = new Crate(300);", "public class Crate { public Crate(Small s) { } }")]
    [InlineData("var c = new Crate();", "public class Crate { public Crate(Small s) { } public Crate() : this(300) { } }")]
    [InlineData("var shelf = new Shelf(); int r = shelf[300];", "public class Shelf { public int this[byte slot] where slot <= 10 { get { return slot; } } }")]
    [InlineData("var shelf = new Shelf(); int r = shelf[x];", "public class Shelf { public int this[byte slot] where slot <= 10 { get { return slot; } } }")]
    public void AValueAssignabilityRefuses_IsLeftToAssignability(string statement, string extraTypes)
    {
        AssertSilent(statement, extraTypes);
    }

    [Fact]
    public void AnIndexIsHeldOnlyToThePredicateOfTheIndexerItSelects()
    {
        AssertSilent(
            "var pick = new Pick(); string r = pick[-1];",
            "public class Pick { public string this[long l] where l >= 0 { get { return \"long\"; } } public string this[int i] { get { return \"int\"; } } }");
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