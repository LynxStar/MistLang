using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "3")]
public sealed class RegexLiteralSugarTests
{
    [Theory]
    [InlineData("regex bound = (regex)\"a+\";", "a+")]
    [InlineData("var bound = new Regex(\"a+\");", "a+")]
    [InlineData("regex bound = new(\"a+\");", "a+")]
    [InlineData("regex bound = (regex)\"\\\\d+\";", @"\\d+")]
    public void AStringLiteralCastOrConstructedIntoARegex_BindsAsARegexLiteral(string statement, string body)
    {
        Assert.Equal(body, Assert.IsType<RegexLiteralExpression>(Bound(statement)).Pattern);
    }

    [Theory]
    [InlineData("regex bound = (regex)text;", typeof(CastExpression))]
    [InlineData("regex bound = (regex)$\"{text}\";", typeof(CastExpression))]
    [InlineData("var bound = new Regex(text);", typeof(NewExpression))]
    public void AStringReadAtRunTime_KeepsItsRuntimeConstruction(string statement, Type bound)
    {
        Assert.IsType(bound, Bound(statement));
    }

    private static Expression Bound(string statement)
    {
        var model = StandardModelProbe.Build($"string text = \"a+\"; {statement}", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var declaration = probe.MethodsList.Single(method => method.Name == "Run").Body!.Statements.OfType<LocalDeclarationStatement>().Last();

        return Assert.IsAssignableFrom<Expression>(declaration.Initializer);
    }
}