using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinedReturnContractDecisionTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Int32", "positive", "positive")]
    [InlineData("Int32", "nonnegative", "positive")]
    [InlineData("Int32", "zero through one hundred", "one through nine")]
    [InlineData("Int32", "nonzero", "positive")]
    [InlineData("Double", "positive", "positive")]
    [InlineData("Double", "nonnegative", "positive")]
    [InlineData("Double", "closed unit", "open unit")]
    [InlineData("Double", "complement of positive", "nonpositive")]
    [InlineData("Double", "complement of positive", "complement of positive")]
    [InlineData("Double", "complement of nonpositive", "positive")]
    [InlineData("Single", "nonnegative", "positive")]
    [InlineData("Single", "complement of positive", "nonpositive")]
    [InlineData("Single", "at most float tenth", "at most double tenth")]
    [InlineData("Decimal", "nonnegative", "positive")]
    [InlineData("Decimal", "nonpositive", "complement of positive")]
    public void ASameOrStrongerDeclaredReturn_IsAdmitted(string domain, string promised, string produced)
    {
        Assert.Null(DeclaredReturn(domain, promised, produced));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Int32", "positive", "nonnegative")]
    [InlineData("Int32", "zero through nine", "zero through ten")]
    [InlineData("Int32", "positive", "")]
    [InlineData("Double", "positive", "nonnegative")]
    [InlineData("Double", "half-open unit", "closed unit")]
    [InlineData("Double", "nonpositive", "complement of positive")]
    [InlineData("Double", "positive", "complement of nonpositive")]
    [InlineData("Double", "positive", "")]
    [InlineData("Single", "positive", "nonnegative")]
    [InlineData("Single", "nonpositive", "complement of positive")]
    [InlineData("Single", "at most double tenth", "at most float tenth")]
    [InlineData("Single", "positive", "")]
    [InlineData("Decimal", "positive", "nonnegative")]
    [InlineData("Decimal", "half-open unit", "closed unit")]
    [InlineData("Decimal", "positive", "")]
    public void AWeakerOrUnrefinedDeclaredReturn_IsRefused(string domain, string promised, string produced)
    {
        Assert.Equal("MIST0284", DeclaredReturn(domain, promised, produced)?.Number.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Letters", "Letters")]
    [InlineData("Letters", "NonEmptyLetters")]
    [InlineData("BelowLimit", "BelowLimit")]
    [InlineData("BelowLimit", "NonNegativeBelowLimit")]
    public void AnUnreadablePredicatePreservedByRefinedIdentity_IsAdmitted(string promised, string produced)
    {
        Assert.Null(OpaqueReturn(promised, produced));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Letters", "SameLetters")]
    [InlineData("Letters", "Digits")]
    [InlineData("Letters", "string")]
    [InlineData("BelowLimit", "OtherBelowLimit")]
    [InlineData("BelowLimit", "int")]
    public void AnUnreadablePredicateWithoutRefinedIdentity_IsRefused(string promised, string produced)
    {
        Assert.Equal("MIST0284", OpaqueReturn(promised, produced)?.Number.Value);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("Int32", "literal")]
    [InlineData("Int32", "two positive returns")]
    [InlineData("Int32", "refined call")]
    public void ASupportedIntegralLiftMeetsTheReturnContract(string domain, string body)
    {
        Assert.Null(LiftedReturn(domain, body));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("Int32", "negative literal")]
    [InlineData("Int32", "one negative return")]
    [InlineData("Int32", "local")]
    [InlineData("Int32", "sum")]
    [InlineData("Double", "negative literal")]
    [InlineData("Double", "literal")]
    [InlineData("Single", "literal")]
    [InlineData("Decimal", "literal")]
    public void AnInsufficientOrUnsupportedBodyLift_IsRefused(string domain, string body)
    {
        Assert.Equal("MIST0284", LiftedReturn(domain, body)?.Number.Value);
    }

    private static MistDiagnostic? DeclaredReturn(string domain, string promised, string produced)
    {
        var contract = new NumericContract(domain);
        return RefinementContracts.ReturnFinding(contract.TypeFor(produced, "Produced"), contract.TypeFor(promised, "Promised"));
    }

    private static MistDiagnostic? OpaqueReturn(string promised, string produced)
    {
        var h = new HandBuiltRefinements();
        var letters = h.Regex("[a-z]+", "Letters");
        var limits = HandBuiltModel.Class("Limits");
        var field = new Field("Bound", h.Int, default, DeclarationFragments.Public with { IsStatic = true, IsReadonly = true })
        { DeclaringType = limits };
        var access = new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), limits),
            "Bound", h.Int, member: new FieldResult(field, limits, []));
        var belowLimit = h.Refine(h.Int, ComparisonKind.LessThan, access, "BelowLimit");
        var source = produced switch
        {
            "Letters" => letters,
            "NonEmptyLetters" => HandBuiltRefinements.RefinePattern(letters,
                new NegatedPattern(HandBuiltModel.Node(), h.String, h.Compare(h.String, ComparisonKind.Equal, "")), produced),
            "SameLetters" => h.Regex("[a-z]+", produced),
            "Digits" => h.Regex("[0-9]+", produced),
            "string" => h.String,
            "BelowLimit" => belowLimit,
            "NonNegativeBelowLimit" => h.Refine(belowLimit, ComparisonKind.GreaterThanOrEqual, 0L, produced),
            "OtherBelowLimit" => h.Refine(h.Int, ComparisonKind.LessThan, access, produced),
            "int" => h.Int,
            _ => throw new ArgumentOutOfRangeException(nameof(produced)),
        };
        return RefinementContracts.ReturnFinding(source, promised == "Letters" ? letters : belowLimit);
    }

    private static MistDiagnostic? LiftedReturn(string domain, string body)
    {
        var h = new HandBuiltRefinements();
        var type = h.Built.Type(domain);
        var promised = h.Refine(type, ComparisonKind.GreaterThan, NumericContract.Number(domain, 0), "Positive");
        var owner = HandBuiltModel.Class("Factory");
        Block returns;
        var one = h.Literal(NumericContract.Number(domain, 1));
        var negative = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, one, type);
        switch(body)
        {
            case "literal":
            case "negative literal":
                returns = DeclarationFragments.Returning(body == "literal" ? one : negative);
                break;
            case "two positive returns":
            case "one negative return":
                var flag = new Field("Flag", h.Bool, default, DeclarationFragments.Public) { DeclaringType = owner };
                returns = HandBuiltModel.Block(HandBuiltRefinements.If(new FieldReferenceExpression(HandBuiltModel.Node(), flag),
                    new ReturnStatement(one, type)), new ReturnStatement(body == "one negative return" ? negative : h.Literal(2L), type));
                break;
            case "refined call":
                var keep = new Method("Keep", promised, [], DeclarationFragments.Public)
                { DeclaringType = owner, Body = DeclarationFragments.Returning(one) };
                returns = DeclarationFragments.Returning(new InvocationExpression(HandBuiltModel.Node(),
                    new MethodReferenceExpression(HandBuiltModel.Node(), keep), [], promised, keep, []));
                break;
            case "local":
                var held = HandBuiltRefinements.Local(type, "held");
                returns = HandBuiltModel.Block(HandBuiltRefinements.Give(held, one), new ReturnStatement(held, type));
                break;
            case "sum":
                returns = DeclarationFragments.Returning(h.Binary(one, BinaryOperatorKind.Add, one, type));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(body));
        }

        var make = new Method("Make", type, [], DeclarationFragments.Public) { DeclaringType = owner, Body = returns };
        return RefinementContracts.ReturnFinding(type, promised, lifted: new LiftedReturns().Of(make));
    }

    internal static ImmutableArray<Diagnostic> Analyse(string source)
    {
        var model = StandardModelProbe.Build("", out var buildDiagnostics, source);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var nodes = SourceBodies.Of(model)
            .Where(root => root.Owner?.FullyQualifiedName.StartsWith("Probe.", StringComparison.Ordinal) == true)
            .SelectMany(root => BodyWalker.DescendantsAndSelf(root.Root));
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}