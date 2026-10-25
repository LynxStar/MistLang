using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.PatternAssignmentFixture;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PatternAssignmentTests
{
    [Fact]
    public void OrBypassReportsThePatternVariableAtItsRead()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable("log");

        var diagnostic = Assert.Single(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), Use(Read(variable)))));

        AssertRead(diagnostic, "log");
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(38, diagnostic.Location.Start.Column);
        Assert.Equal(PipelineStage.Analysis, diagnostic.Stage);
        Assert.Equal("flow-framework", diagnostic.AnalyzerName);
    }

    [Theory]
    [InlineData("is")]
    [InlineData("and-before")]
    [InlineData("and-after")]
    [InlineData("double-negation")]
    public void SuccessfulBindingAssignsTheVariable(string conditionKind)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var match = fixture.Match(variable);
        var condition = conditionKind switch
        {
            "and-before" => fixture.And(fixture.Flag(), match),
            "and-after" => fixture.And(match, fixture.Positive(variable)),
            "double-negation" => fixture.Not(fixture.Not(match)),
            _ => (Expression)match,
        };

        Assert.Empty(fixture.Analyze(If(condition, Use(Read(variable)))));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ConditionReadsFollowShortCircuitAndNegation(bool negated, bool conjunction, bool admitted)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        Expression left = fixture.Match(variable);
        if(negated)
        {
            left = fixture.Not(left);
        }

        var condition = conjunction ? fixture.And(left, fixture.Positive(variable)) : fixture.Or(left, fixture.Positive(variable));
        var diagnostics = fixture.Analyze(If(condition));

        if(admitted)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            AssertRead(Assert.Single(diagnostics), variable.Name);
        }
    }

    [Fact]
    public void NegatedMatchDoesNotAssignItsThenBranch()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();

        AssertRead(Assert.Single(fixture.Analyze(If(fixture.Not(fixture.Match(variable)), Use(Read(variable))))), variable.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JoinRequiresAssignmentOnEveryReachingPath(bool bothBranches)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var assign = new IfStatement(fixture.Flag(), HandBuiltModel.Block(Write(variable)),
            bothBranches ? HandBuiltModel.Block(Write(variable)) : null);
        var diagnostics = fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), assign, Use(Read(variable))));

        if(bothBranches)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            AssertRead(Assert.Single(diagnostics), variable.Name);
        }
    }

    [Fact]
    public void ReturningBranchDoesNotRemoveAssignmentFromTheJoin()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var assignOrReturn = new IfStatement(fixture.Flag(), HandBuiltModel.Block(Write(variable)),
            HandBuiltModel.Block(new ReturnStatement(null, fixture.Integer)));

        Assert.Empty(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), assignOrReturn, Use(Read(variable)))));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LoopBodyRequiresBindingOnItsEntry(bool forLoop, bool bypass)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        Expression condition = fixture.Match(variable);
        if(bypass)
        {
            condition = fixture.Or(fixture.Flag(), condition);
        }

        var body = HandBuiltModel.Block(Use(Read(variable)), Write(variable));
        Statement loop = forLoop ? new ForStatement(null, condition, null, body) : new WhileStatement(condition, body, conditionBinds: true);
        var diagnostics = fixture.Analyze(loop);

        if(bypass)
        {
            AssertRead(Assert.Single(diagnostics), variable.Name);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchGuardBindingMustReachTheResult(bool bypass)
    {
        var fixture = new PatternAssignmentFixture();
        var armVariable = fixture.Variable("arm");
        var guardVariable = fixture.Variable("guard");
        var guard = bypass ? fixture.Or(fixture.Flag(), fixture.Match(guardVariable)) : fixture.And(fixture.Flag(), fixture.Match(guardVariable));
        var expression = new SwitchExpression(HandBuiltModel.Node(), Read(fixture.Variable("input")),
            [new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), fixture.Integer, armVariable),
                fixture.And(fixture.Positive(armVariable), guard), Read(guardVariable)),
             new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), fixture.Integer, null), null,
                HandBuiltModel.Literal(fixture.Integer, 0L))], fixture.Integer);
        var diagnostics = fixture.Analyze(Use(expression));

        if(bypass)
        {
            AssertRead(Assert.Single(diagnostics), "guard");
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData("var")]
    [InlineData("tuple")]
    [InlineData("conjunction")]
    [InlineData("property")]
    [InlineData("list-property-tuple")]
    [InlineData("list-slice")]
    public void NestedBindingsAreAssignedOnlyAfterMatching(string kind)
    {
        var fixture = new PatternAssignmentFixture();
        var (pattern, variables) = fixture.NestedPattern(kind);
        var reads = variables.Select(variable => Use(Read(variable))).ToArray();

        Assert.Empty(fixture.Analyze(If(fixture.Test(pattern), reads)));
        var diagnostics = fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Test(pattern)), reads));

        Assert.Equal(variables.Count, diagnostics.Count);
        foreach(var (variable, diagnostic) in variables.Zip(diagnostics))
        {
            AssertRead(diagnostic, variable.Name);
        }
    }

    [Theory]
    [InlineData("lambda", false)]
    [InlineData("lambda", true)]
    [InlineData("async-lambda", false)]
    [InlineData("async-lambda", true)]
    [InlineData("async", false)]
    [InlineData("async", true)]
    [InlineData("function", false)]
    [InlineData("function", true)]
    public void NestedBodyStartsItsOwnPatternVariablesUnassigned(string kind, bool bypass)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        Expression condition = fixture.Match(variable);
        if(bypass)
        {
            condition = fixture.Or(fixture.Flag(), condition);
        }

        var nested = fixture.Wrap(kind, HandBuiltModel.Block(If(condition, Use(Read(variable)))));
        var diagnostics = fixture.Analyze(nested);

        if(bypass)
        {
            AssertRead(Assert.Single(diagnostics), variable.Name);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData("lambda")]
    [InlineData("async-lambda")]
    [InlineData("async")]
    [InlineData("function")]
    public void FunctionDoesNotPreassignPatternsInItsNestedBodies(string innerKind)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var inner = fixture.Wrap(innerKind, HandBuiltModel.Block(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), Use(Read(variable)))));

        AssertRead(Assert.Single(fixture.Analyze(fixture.Wrap("function", inner))), variable.Name);
    }

    [Theory]
    [InlineData("lambda", false)]
    [InlineData("lambda", true)]
    [InlineData("async", false)]
    [InlineData("async", true)]
    [InlineData("function", false)]
    [InlineData("function", true)]
    public void CapturedPatternUsesTheEnclosingAssignmentState(string kind, bool bypass)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        Expression condition = fixture.Match(variable);
        if(bypass)
        {
            condition = fixture.Or(fixture.Flag(), condition);
        }

        var nested = fixture.Wrap(kind, HandBuiltModel.Block(Use(Read(variable))));
        var diagnostics = fixture.Analyze(If(condition, nested));

        if(!bypass)
        {
            Assert.Empty(diagnostics);
        }
        else if(kind == "function")
        {
            AssertEntry(Assert.Single(diagnostics), variable.Name);
        }
        else
        {
            AssertRead(Assert.Single(diagnostics), variable.Name);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FunctionCaptureIsCheckedAtCallOrConversionIncludingHoistedEntries(bool conversion, bool assigned)
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var function = fixture.Function(HandBuiltModel.Block(Use(Read(variable))));
        Expression entry = conversion ? fixture.Reference(function) : fixture.Call(function);
        var body = assigned
            ? HandBuiltModel.Block(function, Write(variable), Use(entry))
            : HandBuiltModel.Block(Use(entry), function);
        var diagnostics = fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), body));

        if(assigned)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            AssertEntry(diagnostic, variable.Name);
            Assert.Equal(38, diagnostic.Location.Start.Column);
        }
    }

    [Fact]
    public void TransitiveFunctionCaptureRemainsCheckedAtEntry()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var reader = fixture.Function(HandBuiltModel.Block(Use(Read(variable))));
        var forward = fixture.Function(HandBuiltModel.Block(Use(fixture.Call(reader))), "Forward");

        AssertEntry(Assert.Single(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)),
            reader, forward, Use(fixture.Call(forward))))), variable.Name);
    }

    [Fact]
    public void InnerFunctionCapturesItsOuterFunctionsAssignedPattern()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();
        var inner = fixture.Wrap("function", HandBuiltModel.Block(Use(Read(variable))));
        var outer = fixture.Wrap("function", HandBuiltModel.Block(If(fixture.Match(variable), inner)));

        Assert.Empty(fixture.Analyze(outer));
    }

    [Fact]
    public void PatternBindingAssignsEveryStructSlot()
    {
        var fixture = new PatternAssignmentFixture();
        var (variable, first, second) = fixture.StructVariable();

        Assert.Empty(fixture.Analyze(If(fixture.Match(variable), Use(FieldRead(variable, first)), Use(FieldRead(variable, second)), Use(Read(variable)))));
    }

    [Fact]
    public void PartialWriteAfterBypassDoesNotAssignSiblingStorage()
    {
        var fixture = new PatternAssignmentFixture();
        var (variable, first, second) = fixture.StructVariable();
        var write = Use(new AssignmentExpression(HandBuiltModel.Node(), FieldRead(variable, first),
            HandBuiltModel.Literal(fixture.Integer, 1L), fixture.Integer));

        AssertRead(Assert.Single(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), write,
            Use(FieldRead(variable, first)), Use(FieldRead(variable, second))))), "point.second");
    }

    [Fact]
    public void RepeatedUnassignedReadsDoNotCascade()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();

        AssertRead(Assert.Single(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)),
            Use(Read(variable)), Use(Read(variable, 61))))), variable.Name);
    }

    [Fact]
    public void FailedReadDoesNotAddAnAssignmentDiagnostic()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();

        Assert.Empty(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)), Use(Read(variable, narrowed: ErrorType.Instance)))));
    }

    [Fact]
    public void UnreachablePatternReadIsNotJudged()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable();

        Assert.Empty(fixture.Analyze(If(fixture.Or(fixture.Flag(), fixture.Match(variable)),
            new ReturnStatement(null, fixture.Integer), Use(Read(variable)))));
    }

    [Fact]
    public void OrdinaryDeclarationsStillResetInsideFunctions()
    {
        var fixture = new PatternAssignmentFixture();
        var variable = fixture.Variable("ordinary");
        var pattern = fixture.Variable();
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(variable, null),
            If(fixture.Match(pattern), Use(Read(pattern)), Use(Read(variable)), Write(variable)));

        AssertRead(Assert.Single(fixture.Analyze(fixture.Wrap("function", body))), "ordinary");
    }

    [Fact]
    public void InitializedAndIntroducedBindingsRemainAssigned()
    {
        var fixture = new PatternAssignmentFixture();
        var declared = fixture.Variable("declared");
        var item = fixture.Variable("item");
        var part = fixture.Variable("part");
        var ambient = fixture.Variable("ambient");
        var parameter = fixture.Variable("parameter");
        var pattern = fixture.Variable();
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(declared, HandBuiltModel.Literal(fixture.Integer, 1L)),
            new ForEachStatement(item, HandBuiltModel.Literal(fixture.ArrayOf(fixture.Integer)), HandBuiltModel.Block(Use(Read(item)))),
            new TupleDeconstructionStatement([part, fixture.Variable("otherPart")], fixture.Pair()), Use(Read(part)),
            new WithBlockStatement(HandBuiltModel.Literal(fixture.Integer), HandBuiltModel.Block(Use(Read(ambient))), ambient),
            Use(Read(declared)), Use(Read(parameter)), If(fixture.Match(pattern), Use(Read(pattern))));

        Assert.Empty(fixture.Analyze(fixture.Function(body, parameters: [parameter])));
    }

    private static void AssertRead(Diagnostic diagnostic, string name)
    {
        Assert.Equal("MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT", diagnostic.Number.Value);
        Assert.Equal($"Local variable '{name}' is read before being definitely assigned.", diagnostic.Message);
    }

    private static void AssertEntry(Diagnostic diagnostic, string name)
    {
        Assert.Equal("MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED", diagnostic.Number.Value);
        Assert.Contains($"reads '{name}'", diagnostic.Message, StringComparison.Ordinal);
    }
}