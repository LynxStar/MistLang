// Tier 2 (Functional): bound expressions and nested bodies are walked by their single reporting rule.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OperatorPositionSiteTests
{
    [Theory]
    [InlineData("static-lambda-captures-a-field", "MIST0211")]
    [InlineData("static-lambda-captures-a-local", "MIST0211")]
    [InlineData("static-local-function-captures-a-field", "MIST0212")]
    [InlineData("static-local-function-captures-a-local", "MIST0212")]
    public void RefusedCaptureKeepsItsDiagnostic(string caseName, string expected)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        Expression capture = caseName.EndsWith("field", StringComparison.Ordinal)
            ? new FieldReferenceExpression(HandBuiltModel.Node(37), new Field("seed", integer, default, default))
            : new LocalReferenceExpression(HandBuiltModel.Node(37), new LocalVariable("seed", integer));
        object root = caseName.StartsWith("static-lambda", StringComparison.Ordinal)
            ? new LambdaExpression(HandBuiltModel.Node(3), [], capture, null, true, model.Interner.GetOrCreateFunctionType([], integer))
            : new LocalFunctionStatement(new Method("Read", integer, [], new MemberModifiers { IsStatic = true })
            {
                Body = HandBuiltModel.Block(new ReturnStatement(capture, integer)),
            }, []);

        AssertSite(model.Analyze(new PositionAnalyzer(), root), expected);
    }

    [Theory]
    [InlineData("binary", "MIST0200")]
    [InlineData("unknown-binary", "MIST0201")]
    [InlineData("conditional", "MIST0205")]
    [InlineData("unknown-member", "MIST0202")]
    public void OperatorReportsAtTheRefusedExpression(string shape, string expected)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var dog = HandBuiltModel.Class("Dog");
        dog.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        Expression root = shape switch
        {
            "binary" => new BinaryExpression(HandBuiltModel.Node(37), HandBuiltModel.Literal(dog), BinaryOperatorKind.Add,
                HandBuiltModel.Literal(integer), ErrorType.Instance),
            "unknown-binary" => new BinaryExpression(HandBuiltModel.Node(37), HandBuiltModel.Literal(UnknownType.Instance), BinaryOperatorKind.Add,
                HandBuiltModel.Literal(integer), ErrorType.Instance),
            "conditional" => new MemberAccessExpression(HandBuiltModel.Node(37), HandBuiltModel.Literal(dog), "Count", ErrorType.Instance, isConditional: true),
            _ => new MemberAccessExpression(HandBuiltModel.Node(37), HandBuiltModel.Literal(UnknownType.Instance), "Count", ErrorType.Instance),
        };

        AssertSite(model.Analyze(new OperatorAnalyzer(), root), expected);
    }

    [Theory]
    [InlineData("coalescing", "MIST0206")]
    [InlineData("readonly", "MIST0207")]
    [InlineData("optional", "MIST0213")]
    public void PositionReportsAtTheRefusedExpression(string shape, string expected)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        object root;
        if(shape == "optional")
        {
            model.Model.OptionalApplications.Record(model.Interner.GetOrCreateUnionType([integer, model.Type("Void")]), HandBuiltModel.Node(37));
            root = HandBuiltModel.Block();
        }
        else
        {
            var local = new LocalVariable("target", integer) { IsReadonly = shape == "readonly" };
            var assignment = new AssignmentExpression(HandBuiltModel.Node(37), new LocalReferenceExpression(HandBuiltModel.Node(), local),
                HandBuiltModel.Literal(integer, 1L), integer, shape == "coalescing");
            root = shape == "coalescing"
                ? new BinaryExpression(HandBuiltModel.Node(3), assignment, BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 1L), integer)
                : new ExpressionStatement(assignment);
        }

        AssertSite(model.Analyze(new PositionAnalyzer(), root), expected);
    }

    [Fact]
    public void StaticBodiesOwnTheirParametersAndLocalsAndMayReadStaticFields()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var parameter = new LocalVariable("arg", integer);
        var local = new LocalVariable("own", integer);
        var body = HandBuiltModel.Block(
            new LocalDeclarationStatement(local, HandBuiltModel.Literal(integer)),
            new ExpressionStatement(new LocalReferenceExpression(HandBuiltModel.Node(), parameter)),
            new ExpressionStatement(new LocalReferenceExpression(HandBuiltModel.Node(), local)),
            new ExpressionStatement(new FieldReferenceExpression(HandBuiltModel.Node(), new Field("Shared", integer, default, new MemberModifiers { IsStatic = true }))));
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [parameter], null, body, true, model.Interner.GetOrCreateFunctionType([integer], integer));
        var function = new LocalFunctionStatement(new Method("Read", integer, [], new MemberModifiers { IsStatic = true }) { Body = body }, [parameter]);

        Assert.Empty(model.Analyze(new PositionAnalyzer(), lambda));
        Assert.Empty(model.Analyze(new PositionAnalyzer(), function));
    }

    [Fact]
    public void AdmittedStatementPositionsAndForeignOptionalApplicationsRemainSilent()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var optional = model.Interner.GetOrCreateUnionType([integer, model.Type("Void")]);
        model.Model.OptionalApplications.Record(optional, HandBuiltModel.Node(37, "ambient.mist"));
        model.Model.OptionalApplications.Record(integer, HandBuiltModel.Node(37));
        var target = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("target", integer));
        var assignment = new AssignmentExpression(HandBuiltModel.Node(37), target, HandBuiltModel.Literal(integer), integer, true);

        Assert.Empty(model.Analyze(new PositionAnalyzer(), new ExpressionStatement(assignment)));
        Assert.Empty(model.Analyze(new OperatorAnalyzer(), new BinaryExpression(HandBuiltModel.Node(37),
            HandBuiltModel.Literal(integer), BinaryOperatorKind.Add, HandBuiltModel.Literal(integer), integer)));
        Assert.Empty(model.Analyze(new OperatorAnalyzer(), new MemberAccessExpression(HandBuiltModel.Node(37),
            HandBuiltModel.Literal(optional), "Count", integer, isConditional: true)));
        Assert.Empty(model.Analyze(new OperatorAnalyzer(), new BinaryExpression(HandBuiltModel.Node(37),
            HandBuiltModel.Literal(ErrorType.Instance), BinaryOperatorKind.Add, HandBuiltModel.Literal(integer), ErrorType.Instance)));
    }

    private static void AssertSite(IReadOnlyList<Diagnostic> diagnostics, string expected)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expected, diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(1, diagnostic.Position().Start.Line);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }
}