using System.Collections.Immutable;

namespace Mist.Tests.Backend;

// A declared async call and a promoted sync call have different bound shapes but require the same C# parentheses.
[Trait("Tier", "4")]
public sealed class AwaitedCallParenthesesTests
{
    private static readonly string[] Callees = ["Declared", "Inferred"];
    private static readonly Lazy<ImmutableDictionary<string, string>> Bodies =
        new(() => ParenthesesMethodBodies.Emit(Statements(), "var f = new Foo();", Types));

    private const string Invariant = "System.Globalization.CultureInfo.InvariantCulture";

    private const string Types = """
        public class Foo
        {
            public async int Declared() { return 7; }
            public async bool DeclaredFlag() { return true; }
            public async string DeclaredText() { return "seven"; }
            public async string? DeclaredMaybe() { return "seven"; }
            public async Row DeclaredRow() { return new Row(); }

            public int Inferred() { return Declared(); }
            public bool InferredFlag() { return DeclaredFlag(); }
            public string InferredText() { return DeclaredText(); }
            public string? InferredMaybe() { return DeclaredMaybe(); }
            public Row InferredRow() { return DeclaredRow(); }

            public static int Take(int value) { return value; }
        }

        public class Row
        {
            public int this[int i] { get { return i + 7; } }
        }
        """;

    public static TheoryData<string, string> PostfixOperands => new()
    {
        { "var text = f.@().ToString();", "string text = (await f.@()).ToString(" + Invariant + ");" },
        { "var length = f.@Text().Length;", "int length = (await f.@Text()).Length;" },
        { "var length = f.@Maybe()?.Length;", "int? length = (await f.@Maybe())?.Length;" },
        { "var item = f.@Row()[0];", "int item = (await f.@Row())[0];" },
        { "var joined = \"v\" + f.@();", "string joined = \"v\" + (await f.@()).ToString(" + Invariant + ");" },
    };

    [Theory]
    [MemberData(nameof(PostfixOperands))]
    public void AnAwaitedCallUnderAPostfixOperator_IsParenthesized(string statement, string expected)
    {
        AssertEmittedForBothCallees(statement, expected);
    }

    public static TheoryData<string, string> TightlyBoundOperands => new()
    {
        { "var sum = f.@() + 1;", "int sum = await f.@() + 1;" },
        { "var sum = 1 + f.@();", "int sum = 1 + await f.@();" },
        { "var negated = -f.@();", "int negated = -await f.@();" },
        { "var inverted = !f.@Flag();", "bool inverted = !await f.@Flag();" },
        { "var text = $\"v={f.@()}\";", "$\"v={await f.@()}\"" },
        { "var taken = Foo.Take(f.@());", "int taken = Probe.Foo.Take(await f.@());" },
        { "var matched = f.@() is 7;", "bool matched = await f.@() is 7;" },
        { "var named = f.@() switch { 7 => \"seven\", _ => \"other\" };", "string named = await f.@() switch {" },
        { "var held = f.@Maybe() ?? \"none\";", "string held = await f.@Maybe() ?? \"none\";" },
        { "var value = f.@();", "int value = await f.@();" },
        { "f.@();", "await f.@();" },
    };

    [Theory]
    [MemberData(nameof(TightlyBoundOperands))]
    public void AnAwaitedCallThatAlreadyBindsTightly_IsLeftBare(string statement, string expected)
    {
        AssertEmittedForBothCallees(statement, expected);
    }

    public static TheoryData<string, string> SynchronousLambdas => new()
    {
        { "func<int> blocked = () => f.Inferred();", "() => f.Inferred().GetAwaiter().GetResult();" },
        { "func<string> blocked = () => f.Inferred().ToString();", "() => f.Inferred().GetAwaiter().GetResult().ToString(" + Invariant + ");" },
    };

    [Theory]
    [MemberData(nameof(SynchronousLambdas))]
    public void APromotedCallInsideASyncLambda_BlocksAndIsLeftBare(string statement, string expected)
    {
        Assert.Contains(expected, Bodies.Value[statement], StringComparison.Ordinal);
    }

    private static void AssertEmittedForBothCallees(string statement, string expected)
    {
        foreach(var callee in Callees)
        {
            Assert.Contains(expected.Replace("@", callee), Bodies.Value[statement.Replace("@", callee)], StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> Statements()
    {
        foreach(var row in PostfixOperands.Concat(TightlyBoundOperands))
        {
            var template = Assert.IsType<string>(row[0]);
            foreach(var callee in Callees)
            {
                yield return template.Replace("@", callee);
            }
        }

        foreach(var row in SynchronousLambdas)
        {
            yield return Assert.IsType<string>(row[0]);
        }
    }
}