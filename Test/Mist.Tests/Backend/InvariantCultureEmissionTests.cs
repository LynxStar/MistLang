// Tier 3 (Component): purity Requirement 13.2 over the real Standard sources. Each text conversion, text
// order, case mapping and regular expression Mist performs is emitted naming the invariant culture, which a
// program run under one culture cannot tell from a program that reads the current one. No Roslyn compile
// and no process spawning.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class InvariantCultureEmissionTests
{
    private const string Provider = "System.Globalization.CultureInfo.InvariantCulture";

    private const string RegexType = "System.Text.RegularExpressions.Regex";

    private static readonly Func<string, Type?> ClrType = name =>
        new[] { typeof(object), typeof(Console), typeof(Regex) }.Select(anchor => anchor.Assembly.GetType(name)).FirstOrDefault(type => type is not null);

    [Theory]
    [InlineData("var up = \"i\".ToUpper();", "string up = \"i\".ToUpperInvariant();")]
    [InlineData("var parsed = Convert.ToInt32(\"1234\");", $"int parsed = System.Convert.ToInt32(\"1234\", {Provider});")]
    [InlineData("var shown = 1.5.ToString();", $"string shown = 1.5.ToString({Provider});")]
    [InlineData("var glued = \"d=\" + 1.5;", $"string glued = \"d=\" + 1.5.ToString({Provider});")]
    [InlineData("Console.WriteLine(-2.5);", $"System.Console.WriteLine((-2.5).ToString({Provider}));")]
    [InlineData("var d = 1.5; var hole = $\"d={d}\";", "string hole = System.FormattableString.Invariant($\"d={d}\");")]
    [InlineData("var t = (1.5, 2); var tupled = $\"t={t}\";", "string tupled = $\"t={MistInvariant.Text(t)}\";")]
    [InlineData("unknown u = 1.5; Console.WriteLine(u);", "System.Console.WriteLine(MistInvariant.Text(u));")]
    public void ValueToText_NamesTheInvariantCulture(string statements, string expected)
    {
        Assert.Contains(expected, Emit(statements), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var name = \"x\"; var hole = $\"n={name}\";", "string hole = $\"n={name}\";")]
    [InlineData("var glued = \"a\" + 'b' + true;", "string glued = \"a\" + 'b' + true;")]
    [InlineData("Console.WriteLine(true);", "System.Console.WriteLine(true);")]
    public void TextNoCultureCanChange_IsLeftAsWritten(string statements, string expected)
    {
        Assert.Contains(expected, Emit(statements), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var l = new List<string>(); l.Sort();", "l.Sort(System.StringComparer.InvariantCulture);")]
    [InlineData("var l = new List<unknown>(); l.Sort();", "l.Sort(MistInvariant.Comparer<object>());")]
    [InlineData(
        "var l = new List<(string, int)>(); l.Sort();",
        "l.Sort(MistInvariant.Elements<(string, int)>(System.StringComparer.InvariantCulture, System.Collections.Generic.Comparer<int>.Default));")]
    [InlineData("var l = new List<int>(); l.Sort();", "l.Sort();")]
    public void ListSort_OrdersTextByTheInvariantCulture(string statements, string expected)
    {
        Assert.Contains(expected, Emit(statements), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var r = `(?i)i`;", $"new {RegexType}(\"(?i)i\", {RegexType}Options.Compiled | {RegexType}Options.CultureInvariant)")]
    [InlineData("var r = new Regex(\"a\");", $"new {RegexType}(\"a\", {RegexType}Options.Compiled | {RegexType}Options.CultureInvariant)")]
    [InlineData("var matched = \"I\" is `(?i)i`;", $"{RegexType}.IsMatch(__e")]
    public void ARegularExpression_IsCultureInvariant(string statements, string expected)
    {
        var text = Emit(statements);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains($"{RegexType}Options.CultureInvariant", text, StringComparison.Ordinal);
    }

    private static string Emit(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(run.Body);

        using(IntrinsicSupply.Enter(model, ClrType))
        {
            BodyEmitter.EmitMethodBody(run, run.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}