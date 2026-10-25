using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RegexAnalyzerTests
{
    private const int Written = 40;

    private readonly HandBuiltModel model = new();

    private readonly ClassType text;

    private readonly ClassType boolean;

    public RegexAnalyzerTests()
    {
        text = model.Type("String");
        boolean = model.Type("Boolean");
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("pattern")]
    [InlineData("arm")]
    [InlineData("alias")]
    [InlineData("where")]
    [InlineData("alias-tested")]
    public void AMalformedRegex_IsRefusedOnceWhereItIsWritten(string position)
    {
        var diagnostic = Assert.Single(Analyze(position, "a)|(?:b"));

        Assert.Equal("MIST_PA_REGEX_INVALID", diagnostic.Number.Value);
        Assert.Equal(Written + 1, diagnostic.Location.Start.Column);
        Assert.Contains("offset 2", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("pattern")]
    [InlineData("arm")]
    [InlineData("alias")]
    [InlineData("where")]
    [InlineData("alias-tested")]
    public void AWellFormedRegex_IsSilent(string position)
    {
        Assert.Empty(Analyze(position, "(?x)abc #c"));
    }

    private IReadOnlyList<Diagnostic> Analyze(string position, string body)
    {
        var regex = new RegexPattern(HandBuiltModel.Node(Written), text, body);
        var subject = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("s", text));

        return position switch
        {
            "literal" => model.Analyze(new RegexAnalyzer(), HandBuiltModel.Block(
                new LocalDeclarationStatement(new LocalVariable("r", model.Type("Regex")), new RegexLiteralExpression(HandBuiltModel.Node(Written), body, model.Type("Regex"))))),
            "pattern" => model.Analyze(new RegexAnalyzer(), Test(subject, regex)),
            "arm" => model.Analyze(new RegexAnalyzer(), HandBuiltModel.Block(new ExpressionStatement(new SwitchExpression(HandBuiltModel.Node(), subject,
                [new SwitchArm(HandBuiltModel.Node(), new NegatedPattern(HandBuiltModel.Node(), text, regex), null, HandBuiltModel.Literal(text, "no")),
                 new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), text, null), null, HandBuiltModel.Literal(text, "yes"))], text)))),
            "alias" => model.Analyze(new RegexAnalyzer(), types: [Alias(regex)]),
            "where" => model.Analyze(new RegexAnalyzer(), types: [Guarded(regex)]),
            _ => AliasTested(subject, regex),
        };
    }

    private IReadOnlyList<Diagnostic> AliasTested(Expression subject, RegexPattern regex)
    {
        var alias = Alias(regex);
        var refined = (RefinedType)alias.AliasedType!;
        var tested = new TypeTestPattern(HandBuiltModel.Node(), refined, null, refinement: regex);

        return model.Analyze(new RegexAnalyzer(), HandBuiltModel.Block(Test(subject, tested), Test(subject, tested)), types: [alias]);
    }

    private Block Test(Expression subject, Pattern pattern)
    {
        return HandBuiltModel.Block(new ExpressionStatement(new IsPatternExpression(HandBuiltModel.Node(), subject, pattern, boolean)));
    }

    private TypeAlias Alias(RegexPattern regex)
    {
        return new TypeAlias("Shaped", "Probe", new Scope(null), null, new TypeAliasDeclarationNode())
        {
            AliasedType = new RefinedType(text, HandBuiltModel.Node(), "Shaped") { Predicate = regex },
        };
    }

    private ClassType Guarded(RegexPattern regex)
    {
        var owner = HandBuiltModel.Class("Guard");
        var parameter = new Parameter("p", new RefinedType(text, HandBuiltModel.Node()) { Predicate = regex });
        owner.MethodsList.Add(new Method("Take", text, [parameter], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = owner });

        return owner;
    }
}