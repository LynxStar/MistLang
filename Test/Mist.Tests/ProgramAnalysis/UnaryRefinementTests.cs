using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class UnaryRefinementTests
{
    [Theory]
    [InlineData("integer", "named", "return")]
    [InlineData("integer", "named", "declaration")]
    [InlineData("integer", "named", "argument")]
    [InlineData("integer", "anonymous", "return")]
    [InlineData("integer", "anonymous", "declaration")]
    [InlineData("integer", "anonymous", "argument")]
    [InlineData("integer", "lifted", "return")]
    [InlineData("integer", "lifted", "declaration")]
    [InlineData("integer", "lifted", "argument")]
    [InlineData("boolean", "named", "return")]
    [InlineData("boolean", "named", "declaration")]
    [InlineData("boolean", "named", "argument")]
    [InlineData("boolean", "anonymous", "return")]
    [InlineData("boolean", "anonymous", "declaration")]
    [InlineData("boolean", "anonymous", "argument")]
    [InlineData("boolean", "lifted", "return")]
    [InlineData("boolean", "lifted", "declaration")]
    [InlineData("boolean", "lifted", "argument")]
    public void ATransformedBaseResultMustProveItsDestination(string domain, string form, string site)
    {
        var h = new HandBuiltRefinements();
        var boolean = domain == "boolean";
        var type = boolean ? h.Bool : h.Int;
        var target = h.Refine(type, boolean ? ComparisonKind.Equal : ComparisonKind.GreaterThan,
            boolean ? true : 0L, form == "anonymous" ? null : boolean ? "Yes" : "Positive");
        var input = form == "lifted" ? (IMistType)new LiftedType(type, boolean ? true : 2L) : target;
        var value = new UnaryExpression(HandBuiltModel.Node(24),
            boolean ? UnaryOperatorKind.LogicalNot : UnaryOperatorKind.Negate, HandBuiltRefinements.Local(input), type);

        var finding = Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), At(h, site, value, target)));

        Assert.Equal(form == "anonymous" && site == "argument" ? "MIST_PA_CONSTRAINT_UNPROVEN" : "MIST_PA_REFINEMENT_UNPROVEN", finding.Number.Value);
        Assert.Equal(25, finding.Location.Start.Column);
    }

    [Theory]
    [InlineData("integer", "return")]
    [InlineData("integer", "declaration")]
    [InlineData("integer", "argument")]
    [InlineData("boolean", "return")]
    [InlineData("boolean", "declaration")]
    [InlineData("boolean", "argument")]
    [InlineData("real", "return")]
    [InlineData("real", "declaration")]
    [InlineData("real", "argument")]
    [InlineData("string", "return")]
    [InlineData("string", "declaration")]
    [InlineData("string", "argument")]
    public void ADeclaredUnaryOperatorCanPromiseARefinedResult(string domain, string site)
    {
        var h = new HandBuiltRefinements();
        var target = Promise(h, domain);
        var owner = HandBuiltModel.Class("Producer");
        var declared = new Operator(OperatorKind.Minus, target, [new Parameter("value", owner)], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
        };
        var value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, HandBuiltRefinements.Local(owner), target)
        {
            Operator = declared,
        };

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), At(h, site, value, target)));
    }

    [Theory]
    [InlineData("return", false, false)]
    [InlineData("declaration", false, false)]
    [InlineData("argument", false, false)]
    [InlineData("return", true, false)]
    [InlineData("declaration", true, false)]
    [InlineData("argument", true, false)]
    [InlineData("return", false, true)]
    [InlineData("declaration", false, true)]
    [InlineData("argument", false, true)]
    [InlineData("return", true, true)]
    [InlineData("declaration", true, true)]
    [InlineData("argument", true, true)]
    public void OnlyADeclaredRealUnaryReturnContractProvesAWeakerDestination(string site, bool forwardThroughBaseLocal, bool intrinsic)
    {
        var h = new HandBuiltRefinements();
        var target = Promise(h, "real");
        var stronger = h.Refine(h.Double, ComparisonKind.GreaterThan, 5.0, "OverFive");
        var owner = HandBuiltModel.Class("Producer");
        var value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, HandBuiltRefinements.Local(owner), stronger)
        {
            Operator = new Operator(OperatorKind.Minus, stronger, [new Parameter("value", owner)], new MemberModifiers { IsStatic = true, IsIntrinsic = intrinsic })
            {
                DeclaringType = owner,
            },
        };
        var root = At(h, site, value, target);
        if(forwardThroughBaseLocal)
        {
            var local = new LocalVariable("produced", h.Double);
            var destination = At(h, site, HandBuiltRefinements.Read(local), target);
            root = HandBuiltModel.Block(new LocalDeclarationStatement(local, value),
                destination is Statement statement ? statement : HandBuiltRefinements.Evaluate((Expression)destination));
        }

        var findings = h.Built.Analyze(new RefinementAnalyzer(), root);
        if(intrinsic)
        {
            Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", Assert.Single(findings).Number.Value);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData("integer", "return")]
    [InlineData("integer", "declaration")]
    [InlineData("integer", "argument")]
    [InlineData("real", "return")]
    [InlineData("real", "declaration")]
    [InlineData("real", "argument")]
    public void SupportedNegatedConstantsProveTheTransformedValue(string domain, string site)
    {
        var h = new HandBuiltRefinements();
        var real = domain == "real";
        var type = real ? h.Double : h.Int;
        var target = Promise(h, domain);
        var value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, h.Literal(real ? (object)-2.5 : -2L, type), type);

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), At(h, site, value, target)));
    }

    private static RefinedType Promise(HandBuiltRefinements h, string domain)
    {
        return domain switch
        {
            "integer" => h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive"),
            "boolean" => h.Refine(h.Bool, ComparisonKind.Equal, true, "Yes"),
            "real" => h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "PositiveReal"),
            "string" => h.Refine(h.String, ComparisonKind.Equal, "x", "Named"),
            _ => throw new InvalidOperationException(domain),
        };
    }

    private static object At(HandBuiltRefinements h, string site, Expression value, IMistType target)
    {
        return site switch
        {
            "return" => new ReturnStatement(value, target),
            "declaration" => HandBuiltRefinements.Need(value, target),
            "argument" => h.Call(value, target),
            _ => throw new InvalidOperationException(site),
        };
    }
}