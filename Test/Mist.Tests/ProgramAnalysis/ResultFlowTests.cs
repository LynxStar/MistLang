using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ResultFlowTests
{
    private readonly ResultModel shapes = new();

    [Fact]
    public void PropagationEvaluatesOnceAndTheFailurePathLeavesAllScopesBeforeReturning()
    {
        var result = shapes.Result(shapes.Integer, shapes.Text);
        var operand = new InvocationExpression(Node(), Literal(shapes.Integer), [], result, null, null);
        var propagation = shapes.Propagate(operand, result);
        var later = Literal(shapes.Integer, 5L);
        var inner = Block(new ExpressionStatement(propagation), new ExpressionStatement(later));
        var outer = Block(inner);
        var graph = FlowGraphBuilder.Build(outer);
        var steps = graph.Blocks.SelectMany(block => block.Steps).ToList();
        _ = Assert.Single(steps, step => ReferenceEquals(step.Node, operand));
        var failure = graph.Blocks.Single(block => block.Steps.Any(step => ReferenceEquals(step.Node, propagation.FailureReturn)));
        Assert.Equal([inner, outer], failure.Steps.Where(step => step.Kind == FlowStepKind.Leave).Select(step => step.Node));
        Assert.DoesNotContain(Reachable(failure).SelectMany(block => block.Steps), step => ReferenceEquals(step.Node, later));
    }

    [Fact]
    public void ALinearResultInspectionCannotBeRepeated()
    {
        var resource = Class("Resource");
        resource.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        var local = new LocalVariable("result", shapes.Result(resource, shapes.Text));
        var reference = new LocalReferenceExpression(Node(), local);
        var body = Block(new LocalDeclarationStatement(local, Literal(local.Type)),
            new ExpressionStatement(shapes.Inspect(reference)), new ExpressionStatement(shapes.Inspect(new LocalReferenceExpression(Node(2), local))));
        var findings = shapes.Built.Analyze(new OwnershipAnalyzer(), body);
        Assert.Contains(findings, finding => finding.Number == Mist.Common.Diagnostics.ProgramAnalysisDiagnostics.UseAfterMove.Number);
    }

    [Fact]
    public void ConstructionTransfersTheOwnedPayloadAndRetainsOneCleanupOwner()
    {
        var resource = Class("Resource");
        resource.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        resource.MethodsList.Add(new Method("Drop", shapes.Void, [], default) { DeclaringType = resource });
        var payload = new LocalVariable("payload", resource);
        var result = new LocalVariable("result", shapes.Result(resource, shapes.Text));
        var declaration = new LocalDeclarationStatement(payload, Literal(resource));
        var wrapped = new ResultConstructionExpression(Node(), new LocalReferenceExpression(Node(), payload), (ProgramModel.Model.Types.ConstructedType)result.Type, ProgramModel.Construction.Binding.ResultCase.Success);
        var destination = new LocalDeclarationStatement(result, wrapped);
        var plan = LocalLifetimes.Plan(Block(declaration, destination));
        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(payload));
        Assert.Contains(plan.MarksAfter(wrapped), mark => ReferenceEquals(mark.Owner, payload) && !mark.Owned);
        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(result));
    }

    [Fact]
    public void ACorrelationWithoutProofIsRefusedByAnalysis()
    {
        var inspection = shapes.Inspection(shapes.Integer, shapes.Text);
        var pattern = new TuplePattern(Node(), [], inspection, null, null) { CorrelatedInput = inspection };
        var expression = new IsPatternExpression(Node(), Literal(inspection), pattern, shapes.Boolean);
        var findings = shapes.Built.Analyze(new ResultAnalyzer(), expression);
        Assert.Contains(findings, finding => finding.Number == Mist.Common.Diagnostics.ProgramAnalysisDiagnostics.TupleCorrelationUnproven.Number);
    }

    [Fact]
    public void AProvenErrorCannotBecomeIdentityThroughACopyButReassignmentInvalidatesThatFact()
    {
        var error = new LocalVariable("error", shapes.Text) { IsKnownResultError = true };
        var copy = new LocalVariable("copy", shapes.Text);
        var bind = new CatchAllPattern(Node(), shapes.Text, error);
        var known = new IsPatternExpression(Node(), Literal(shapes.Text, "failure"), bind, shapes.Boolean);
        var copied = new LocalDeclarationStatement(copy, new LocalReferenceExpression(Node(), error));
        var first = new ResultIdentityExpression(Node(3), new LocalReferenceExpression(Node(3), copy));
        var assigned = new AssignmentExpression(Node(), new LocalReferenceExpression(Node(), copy), Literal(shapes.Text, "plain"), shapes.Text);
        var second = new ResultIdentityExpression(Node(4), new LocalReferenceExpression(Node(4), copy));
        var body = Block(new IfStatement(known, Block(copied, new ExpressionStatement(first), new ExpressionStatement(assigned), new ExpressionStatement(second)), null));
        var findings = shapes.Built.Analyze(new ResultAnalyzer(), body);
        var finding = Assert.Single(findings);
        Assert.Equal(Mist.Common.Diagnostics.ProgramAnalysisDiagnostics.ResultErrorIsNotIdentity.Number, finding.Number);
    }

    [Fact]
    public void AFreshResultInspectionRetainsItsCarrierUntilTheBoundPayloadTransfers()
    {
        var resource = Class("Resource");
        resource.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        resource.MethodsList.Add(new Method("Drop", shapes.Void, [], default) { DeclaringType = resource });
        var sourceType = shapes.Result(resource, shapes.Text);
        var source = new InvocationExpression(Node(), Literal(shapes.Integer), [], sourceType, null, null);
        var payload = new LocalVariable("token", resource);
        var inspected = shapes.Inspect(source);
        var pattern = new TuplePattern(Node(), [shapes.Tag(true), new CatchAllPattern(Node(), resource, payload)],
            shapes.Built.Interner.GetOrCreateTupleType([shapes.True, resource]), null, null);
        var returned = new LocalReferenceExpression(Node(), payload);
        var conditional = new IfStatement(new IsPatternExpression(Node(), inspected, pattern, shapes.Boolean),
            Block(new ReturnStatement(returned, resource)), null);
        var plan = LocalLifetimes.Plan(Block(conditional));
        Assert.True(plan.Covers(source));
        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(source));
        Assert.Contains(plan.MarksAfter(returned), mark => ReferenceEquals(mark.Owner, source) && !mark.Owned);
    }

    [Fact]
    public void AnOwnedFailureReturnKeepsTheEvaluatedCarriersProvenance()
    {
        var resource = Class("OwnedError");
        var result = shapes.Result(shapes.Integer, resource);
        var source = new LocalVariable("source", result) { Ownership = OwnershipKind.Owned };
        var propagation = shapes.Propagate(new LocalReferenceExpression(Node(), source), result);
        var body = Block(new LocalDeclarationStatement(source, Literal(result)));
        var origins = new Origins(body, null, ScopeKind.Readonly);
        Assert.True(OwnershipAnalyzer.YieldsOwned(propagation.FailureReturn.Value!, null, origins));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PassingALinearResultTransfersItsOwnerUnlessTheParameterLends(bool borrowed)
    {
        var resource = Class("Resource");
        resource.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        var result = shapes.Result(resource, shapes.Text);
        var local = new LocalVariable("result", result);
        var parameter = new Parameter("value", result, new ParameterModifier(false, false,
            borrowed ? OwnershipKind.Borrowed : OwnershipKind.Unrestricted, default));
        var method = new Method("Take", shapes.Void, [parameter], new MemberModifiers { IsStatic = true });
        var reference = new LocalReferenceExpression(Node(), local);
        var call = new InvocationExpression(Node(), new MethodReferenceExpression(Node(), method), [reference], shapes.Void, method, [result]);
        var body = Block(new LocalDeclarationStatement(local, Literal(result)), new ExpressionStatement(call),
            new ExpressionStatement(shapes.Inspect(new LocalReferenceExpression(Node(2), local))));
        var findings = shapes.Built.Analyze(new OwnershipAnalyzer(), body);
        Assert.Equal(!borrowed, findings.Any(finding => finding.Number == Mist.Common.Diagnostics.ProgramAnalysisDiagnostics.UseAfterMove.Number));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractingAnOwnedSnapshotPayloadConsumesThatSnapshotUntilReassignment(bool reassign)
    {
        var resource = Class("Resource");
        resource.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        var inspection = shapes.Inspection(resource, shapes.Text);
        var snapshot = new LocalVariable("snapshot", inspection);
        var tuple = shapes.Built.Interner.GetOrCreateTupleType([shapes.True, resource]);
        IfStatement extract()
        {
            var payload = new LocalVariable("payload", resource) { Ownership = OwnershipKind.Owned };
            var pattern = new TuplePattern(Node(), [shapes.Tag(true), new CatchAllPattern(Node(), resource, payload)], tuple, null, null)
            { CorrelatedInput = inspection, Constituent = tuple };
            return new IfStatement(new IsPatternExpression(Node(), new LocalReferenceExpression(Node(), snapshot), pattern, shapes.Boolean), Block(), null);
        }

        var statements = new List<Statement> { new LocalDeclarationStatement(snapshot, Literal(inspection)), extract() };
        if(reassign)
        {
            statements.Add(new ExpressionStatement(new AssignmentExpression(Node(), new LocalReferenceExpression(Node(), snapshot), Literal(inspection), inspection)));
        }

        statements.Add(extract());
        var findings = shapes.Built.Analyze(new OwnershipAnalyzer(), Block([.. statements]));
        Assert.Equal(!reassign, findings.Any(finding => finding.Number == Mist.Common.Diagnostics.ProgramAnalysisDiagnostics.UseAfterMove.Number));
    }

    private static IEnumerable<FlowBlock> Reachable(FlowBlock start)
    {
        var pending = new Stack<FlowBlock>();
        var seen = new HashSet<FlowBlock>();
        pending.Push(start);
        while(pending.TryPop(out var block))
        {
            if(!seen.Add(block))
            {
                continue;
            }

            yield return block;
            foreach(var next in block.Successors)
            {
                pending.Push(next);
            }
        }
    }
}