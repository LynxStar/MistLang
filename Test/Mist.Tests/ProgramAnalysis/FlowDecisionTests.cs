// Tier 3 (Component): what order 10's rules deliberately do NOT report -- the reads that are assigned
// on every path, the fields classic leaves alone, the element types that stamp -- and the reach facts
// the corpus cannot phrase. WHY each is silent, and which of them classic refuses, is recorded once,
// on order 10 of backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class FlowDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT", "MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT",
        "MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED", "MIST_PA_ARRAY_REQUIRES_INITIALIZER",
        "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE",
    ];

    private const string Dog = """
        public class Dog
        {
            public string Name;

            public Dog(string name)
            {
                Name = name;
            }
        }
        """;

    public FlowDecisionTests()
        : base("MistFlow_")
    {
    }

    protected override string Fixtures { get; } = Dog + """

        public struct Point
        {
            public int x;
            public int y;
        }

        public struct Nested
        {
            public Point inner;
            public bool flag;
        }

        public enum Color : byte { Red, Green }

        type PositiveInt = int > 0;
        """;

    protected override string Preamble { get; } = """
        var flag = true;
        int | string mixed = 1;
        int[] items = [1, 2];
        """;

    [Theory]
    [InlineData("int x = 1; Console.WriteLine(x);")]
    [InlineData("int x; x = 1; Console.WriteLine(x);")]
    [InlineData("int x; if(flag) { x = 1; } else { x = 2; } Console.WriteLine(x);")]
    [InlineData("int x; if(flag) { x = 1; } else { return; } Console.WriteLine(x);")]
    [InlineData("int x; if(flag) { return; } x = 2; Console.WriteLine(x);")]
    [InlineData("int x; while(true) { x = 1; break; } Console.WriteLine(x);")]
    [InlineData("int x; if(mixed is int n) { x = n; } else { x = 0; } Console.WriteLine(x);")]
    [InlineData("int x; int y; x = 1; y = x; Console.WriteLine(y);")]
    [InlineData("int x; x = 1; func<int, int> f = y => y + x; Console.WriteLine(f(1));")]
    [InlineData("int x; x = 1; var t = async { Console.WriteLine(x); };")]
    [InlineData("while(flag) { int x; x = 1; Console.WriteLine(x); flag = false; }")]
    [InlineData("if(mixed is int k) { Console.WriteLine(k); }")]
    [InlineData("foreach(var item in items) { Console.WriteLine(item); }")]
    [InlineData("var (a, b) = (1, 2); Console.WriteLine(a + b);")]
    [InlineData("for(var i = 0; i < 2; i = i + 1) { Console.WriteLine(i); }")]
    [InlineData("int x; if(flag && mixed is int m) { x = m; } else { x = 0; } Console.WriteLine(x);")]
    [InlineData("Point p; p.x = 1; p.y = 2; Console.WriteLine(p.x);")]
    [InlineData("int x; func<int, int> f = y => { x = y; return x; }; Console.WriteLine(f(1));")]
    [InlineData("int x; if(true) { return; } Console.WriteLine(x);")]
    public void ALocalAssignedOnEveryPathToItsRead_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("public class A { public Dog owner; public A() { Console.WriteLine(owner.Name); owner = new Dog(\"a\"); } }")]
    [InlineData("public class A { public Dog owner; public A() { Describe(); owner = new Dog(\"a\"); } public string Describe() { return owner.Name; } }")]
    [InlineData("public class A { public Dog owner; public A() { A self = this; owner = new Dog(\"a\"); } }")]
    [InlineData("public class A { public Dog owner; public A() { func<int, int> f = y => y + this.owner.Name.Length; owner = new Dog(\"a\"); } }")]
    [InlineData("public class A { public Dog owner; public A(bool flag) { if(flag) { owner = new Dog(\"a\"); } else { owner = new Dog(\"b\"); } Console.WriteLine(this.owner.Name); } }")]
    [InlineData("public class A { public Dog owner; public A() { this.owner = new Dog(\"a\"); Console.WriteLine(this.owner.Name); } }")]
    [InlineData("public class A { public int count; public string tag; public A() { Console.WriteLine(this.count); Console.WriteLine(this.tag); } }")]
    [InlineData("public class A { public Dog owner = new Dog(\"a\"); public A() { Console.WriteLine(this.owner.Name); } }")]
    [InlineData("public class Base { public int inherited = 1; } public class A : Base { public Dog owner; public A() { Console.WriteLine(this.inherited); owner = new Dog(\"a\"); } }")]
    [InlineData("public class A { public Dog owner; public A() : this(true) { Console.WriteLine(this.owner.Name); } public A(bool flag) { owner = new Dog(\"a\"); } }")]
    [InlineData("public class A { public Dog | void owner; public A() { Console.WriteLine(this.owner is Dog); } }")]
    [InlineData("public class A { public Dog owner; public A() { owner = new Dog(\"a\"); } public void Later() { Console.WriteLine(this.owner.Name); } }")]
    [InlineData("public class A { public int Value { get; set; } public A() { Console.WriteLine(this.Value); Value = 1; } }")]
    [InlineData("public class A { public Dog Pet { get; set; } = new Dog(\"a\"); public A() { Console.WriteLine(this.Pet.Name); } }")]
    [InlineData("public class A { public Dog Pet { get; set; } public A() { Pet = new Dog(\"a\"); Console.WriteLine(this.Pet.Name); } }")]
    [InlineData("public class A { public int Twice { get { return 2; } } public A() { Console.WriteLine(this.Twice); } }")]
    [InlineData("public class A { public Dog owner; public Dog other; public A(bool flag) { owner = new Dog(\"a\"); if(flag) { other = owner; return; } other = new Dog(\"b\"); } }")]
    [InlineData("public class A { public Dog owner; }")]
    [InlineData("public struct S { public Dog held; public S(Dog d) { Console.WriteLine(\"before\"); held = d; } }")]
    [InlineData("public class A<T> { public T item; public int count; public A() { count = 0; } }")]
    [InlineData("public class A<params TArgs> { public TArgs... Item; public A(params TArgs values) { Item... = values...; } }")]
    [InlineData("public class A { public Dog owner; public A() { this.owner = new Dog(\"a\"); } }")]
    public void AConstructorClassicAdmits_IsAdmitted(string types)
    {
        AssertSilent("", types);
    }

    [Theory]
    [InlineData("var a = new int[3];")]
    [InlineData("var a = new bool[0];")]
    [InlineData("var a = new char[3];")]
    [InlineData("var a = new Point[2];")]
    [InlineData("var a = new Nested[2];")]
    [InlineData("var a = new Index[2];")]
    [InlineData("Span<int> a = stackalloc int[2];")]
    [InlineData("Span<Point> a = stackalloc Point[2];")]
    [InlineData("Span<Nested> a = stackalloc Nested[2];")]
    [InlineData("var a = [new Dog(\"a\"), new Dog(\"b\")];")]
    [InlineData("var a = new Array<Dog, 2>(i => new Dog(\"d\"));")]
    public void AnElementTypeThatStamps_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("int x; Console.WriteLine(x + x);")]
    [InlineData("int x; if(flag) { x = 1; } Console.WriteLine(x); Console.WriteLine(x);")]
    [InlineData("var a = new Dog[3]; var b = a;")]
    public void AFindingIsReportedOnce(string statement)
    {
        _ = Assert.Single(Analyse(statement));
    }

    [Theory]
    [InlineData("Console.WriteLine(nothing);")]
    [InlineData("var a = new Nothing[3];")]
    public void AValueThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
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