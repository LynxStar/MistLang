using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Every row is emitted twice, against a callee Mist declares async and against a sync one the backend
// promotes: the model holds an await for the first and none for the second, so only emitted text shows
// that the two are written alike.
[Trait("Tier", "3")]
public sealed class AwaitedCallParenthesesTests
{
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

    [Theory]
    [InlineData("var text = f.@().ToString();", "string text = (await f.@()).ToString(" + Invariant + ");")]
    [InlineData("var length = f.@Text().Length;", "int length = (await f.@Text()).Length;")]
    [InlineData("var length = f.@Maybe()?.Length;", "int? length = (await f.@Maybe())?.Length;")]
    [InlineData("var item = f.@Row()[0];", "int item = (await f.@Row())[0];")]
    [InlineData("var joined = \"v\" + f.@();", "string joined = \"v\" + (await f.@()).ToString(" + Invariant + ");")]
    public void AnAwaitedCallUnderAPostfixOperator_IsParenthesized(string statement, string expected)
    {
        AssertEmittedForBothCallees(statement, expected);
    }

    [Theory]
    [InlineData("var sum = f.@() + 1;", "int sum = await f.@() + 1;")]
    [InlineData("var sum = 1 + f.@();", "int sum = 1 + await f.@();")]
    [InlineData("var negated = -f.@();", "int negated = -await f.@();")]
    [InlineData("var inverted = !f.@Flag();", "bool inverted = !await f.@Flag();")]
    [InlineData("var text = $\"v={f.@()}\";", "$\"v={await f.@()}\"")]
    [InlineData("var taken = Foo.Take(f.@());", "int taken = Probe.Foo.Take(await f.@());")]
    [InlineData("var matched = f.@() is 7;", "bool matched = await f.@() is 7;")]
    [InlineData("var named = f.@() switch { 7 => \"seven\", _ => \"other\" };", "string named = await f.@() switch {")]
    [InlineData("var held = f.@Maybe() ?? \"none\";", "string held = await f.@Maybe() ?? \"none\";")]
    [InlineData("var value = f.@();", "int value = await f.@();")]
    [InlineData("f.@();", "await f.@();")]
    public void AnAwaitedCallThatAlreadyBindsTightly_IsLeftBare(string statement, string expected)
    {
        AssertEmittedForBothCallees(statement, expected);
    }

    [Theory]
    [InlineData("func<int> blocked = () => f.Inferred();", "() => f.Inferred().GetAwaiter().GetResult();")]
    [InlineData("func<string> blocked = () => f.Inferred().ToString();", "() => f.Inferred().GetAwaiter().GetResult().ToString(" + Invariant + ");")]
    public void APromotedCallInsideASyncLambda_BlocksAndIsLeftBare(string statement, string expected)
    {
        Assert.Contains(expected, Emit(statement), StringComparison.Ordinal);
    }

    private static void AssertEmittedForBothCallees(string statement, string expected)
    {
        foreach(var callee in new[] { "Declared", "Inferred" })
        {
            Assert.Contains(expected.Replace("@", callee), Emit(statement.Replace("@", callee)), StringComparison.Ordinal);
        }
    }

    private static string Emit(string statement)
    {
        var model = StandardModelProbe.Build("var f = new Foo();\n" + statement, out var built, Types);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(run.Body);

        using(IntrinsicSupply.Enter(model, Type.GetType))
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(run, run.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}