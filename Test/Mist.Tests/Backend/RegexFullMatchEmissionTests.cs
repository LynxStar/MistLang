using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class RegexFullMatchEmissionTests
{
    private static readonly ClassType Text = Standard("String");

    private static readonly ClassType Boolean = Standard("Boolean");

    private static readonly ClassType Regex = Standard("Regex");

    [Fact]
    [Trait("Tier", "1")]
    public void AFullMatchPattern_AnchorsTheGroupedPatternAtTheEndsOfTheInput()
    {
        const string expected = """
            "\\A(?:" + pattern + "(?#\n(?#))\\z"
            """;

        Assert.Equal(expected, RegexLowering.FullMatchPattern("pattern"));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("pattern")]
    [InlineData("negated")]
    [InlineData("refined")]
    public void ARegexPattern_IsMatchedThroughTheFullMatchPattern(string form)
    {
        var letters = new RegexPattern(null!, Text, "abc");
        Pattern pattern = form switch
        {
            "pattern" => letters,
            "negated" => new NegatedPattern(null!, Text, letters),
            _ => new TypeTestPattern(null!, new RefinedType(Text, null!, "Letters") { Predicate = letters }, null, refinement: letters),
        };

        Assert.Contains(
            RegexLowering.FullMatchPattern(RegexLowering.Quote("abc")),
            Emit(new IsPatternExpression(null!, Local("s", Text), pattern, Boolean)),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AFullMatchCall_IsMatchedThroughTheFullMatchPattern()
    {
        var full = new Method("IsFullMatch", Boolean, [new Parameter("input", Text)], new MemberModifiers { IsIntrinsic = true })
        {
            DeclaringType = Regex,
        };
        var call = new InvocationExpression(
            null!, new MemberAccessExpression(null!, Local("r", Regex), full.Name, Boolean), [Local("s", Text)], Boolean, full, null);

        Assert.Contains(RegexLowering.FullMatchPattern(RegexLowering.PatternOf("r")), Emit(call), StringComparison.Ordinal);
    }

    private static ClassType Standard(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), null, null);
    }

    private static LocalReferenceExpression Local(string name, ClassType type)
    {
        return new LocalReferenceExpression(null!, new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);

        Assert.False(diagnostics.HasErrors);
        return writer.ToString();
    }
}