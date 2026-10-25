// Tier 3: dynamic real proofs reach refinement sites only after binding succeeds, with NaN-aware refusals.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RealFlowDecisionTests
{
    private const string Declarations = """
        type Positive = double > 0.0;
        type NonNegative = double >= 0.0;
        type NonPositive = double <= 0.0;
        type Unit = double >= 0.0 and <= 1.0;
        type PositiveUnit = Unit and > 0.0;
        type StrictUnit = double > 0.0 and < 1.0;
        type Outside = double (< 0.0 or > 1.0);
        type NotZero = double not 0.0;
        type NotPositive = double not > 0.0;
        type FloatUnit = float >= 0.0f and <= 1.0f;
        type FloatTenth = float <= 0.1;
        type Tenth = double <= 0.1;
        type DecimalUnit = decimal >= 0.0m and <= 1.0m;
        type DecimalNonNegative = decimal >= 0.0m;
        type DecimalNonPositive = decimal <= 0.0m;
        type DecimalPositive = decimal > 0.0m;
        """;

    [Theory]
    [InlineData("NonNegative value = unit;")]
    [InlineData("Unit value = positiveUnit;")]
    [InlineData("Positive value = positiveUnit;")]
    [InlineData("void Forward(Outside first) { NotZero value = first; }")]
    [InlineData("void Forward(StrictUnit first) { Unit value = first; }")]
    [InlineData("DecimalNonNegative value = decimalUnit;")]
    [InlineData("Unit value = floatUnit;")]
    public void ADeclaredRealSubset_ProvesAWeakerRefinement(string body)
    {
        Assert.Empty(Analyse(body));
    }

    [Theory]
    [InlineData("if(input > 0.0) { Positive value = input; }")]
    [InlineData("Positive value = (Positive)2.5;")]
    [InlineData("if(input > 0.0) { Positive value = (Positive)input; }")]
    [InlineData("if(0.0 < input) { Positive value = input; }")]
    [InlineData("if(!(!(input > 0.0))) { Positive value = input; }")]
    [InlineData("if(input >= 0.0 && input <= 1.0) { Unit value = input; }")]
    [InlineData("if(0.0 <= input && 1.0 >= input) { Unit value = input; }")]
    [InlineData("if(!(input >= 0.0 && input <= 1.0)) { return; } Unit value = input;")]
    [InlineData("if(input < 0.0 || input > 1.0) { Outside value = input; }")]
    [InlineData("if(0.0 > input || 1.0 < input) { Outside value = input; }")]
    [InlineData("if(input == 0.0) { return; } NotZero value = input;")]
    [InlineData("if(0.0 == input) { return; } NotZero value = input;")]
    [InlineData("if(input != 0.0) { NotZero value = input; }")]
    [InlineData("if(!(input > 0.0)) { NotPositive value = input; }")]
    [InlineData("if(input > 0.0) { return; } NotPositive value = input;")]
    [InlineData("if(input is >= 0.0 and <= 1.0) { Unit value = input; }")]
    [InlineData("if(input is Unit narrowed) { NonNegative value = narrowed; }")]
    [InlineData("if(single >= 0.0f && single <= 1.0f) { Unit value = single; }")]
    [InlineData("if(0.0f <= single && 1.0f >= single) { FloatUnit value = single; }")]
    [InlineData("if(single <= 0.1) { FloatTenth value = single; }")]
    [InlineData("if(single <= 0.1) { Tenth value = single; }")]
    [InlineData("if(amount >= 0.0m && amount <= 1.0m) { DecimalUnit value = amount; }")]
    [InlineData("if(0.0m <= amount && 1.0m >= amount) { DecimalUnit value = amount; }")]
    [InlineData("if(!(amount > 0.0m)) { DecimalNonPositive value = amount; }")]
    [InlineData("if(amount > 0.0m) { return; } DecimalNonPositive value = amount;")]
    [InlineData("if(amount <= 0.0m) { return; } DecimalPositive value = amount;")]
    public void AGuardWithTheSameRuntimeRealSemantics_ProvesTheRefinement(string body)
    {
        Assert.Empty(Analyse(body));
    }

    [Theory]
    [InlineData("double number = 0.25; if(flag) { number = 0.75; } Unit value = number;")]
    [InlineData("double number = 0.25; if(flag) { number = 0.75; } double copy = number; Unit value = copy;")]
    [InlineData("if(flag) { input = 0.25; } else { input = 0.75; } Unit value = input;")]
    [InlineData("float number = 0.25f; if(flag) { number = 0.75f; } double copy = number; Unit value = copy;")]
    [InlineData("decimal number = 0.25m; if(flag) { number = 0.75m; } DecimalUnit value = number;")]
    [InlineData("if(input > 0.0) { double copy = input; input = other; Positive value = copy; }")]
    [InlineData("if(input > 0.0) { var read = () => input; var ignored = read(); Positive value = input; }")]
    [InlineData("if(input > 0.0) { double unrelated = other; var write = () => { unrelated = other; }; write(); Positive value = input; }")]
    [InlineData("double number = 0.25; while(flag) { number = 0.75; flag = false; } Unit value = number;")]
    public void AJoinCopyOrUnrelatedWrite_PreservesTheSupportedRealFact(string body)
    {
        Assert.Empty(Analyse(body));
    }

    [Theory]
    [InlineData("double Take(Positive p) { return p; } if(input > 0.0) { var value = Take(input); }")]
    [InlineData("double Take(double p) where p > 0.0 { return p; } if(input > 0.0) { var value = Take(input); }")]
    [InlineData("Positive Read(double p) { if(p > 0.0) { return p; } return 1.0; }")]
    public void ARealGuard_ReachesArgumentConstraintAndReturnSites(string body)
    {
        Assert.Empty(Analyse(body));
    }

    [Theory]
    [InlineData("if(!(input > 0.0)) { NonPositive value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(!(input <= 0.0)) { Positive value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > 0.0) { return; } NonPositive value = input;", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input >= 0.0) { return; } void TakeNegative(double p) where p < 0.0 { } TakeNegative(input);", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("if(!(input < 0.0) && !(input > 1.0)) { Unit value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input >= 0.0) { Unit value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input >= 0.0 && input <= 1.0) { StrictUnit value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(single <= 0.1f) { Tenth value = single; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("Unit value = input;", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("Unit value = positiveUnit; StrictUnit stricter = value;", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("double number = 0.25; if(flag) { number = 1.5; } Unit value = number;", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > 0.0) { input = other; Positive value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > 0.0) { double copy = input; copy = other; Positive value = copy; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("double copy = input; if(input > 0.0) { Positive value = copy; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > 0.0) { var write = () => { input = other; }; write(); Positive value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > 0.0) { void Write() { input = other; } Write(); Positive value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > 0.0) { double sum = input + 1.0; Positive value = sum; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("void Forward(Positive first) { Positive value = first - first; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("void Forward(Positive first) { var value = (Positive)(first - first); }", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("if(input > limit) { Positive value = input; }", "MIST_PA_REFINEMENT_UNPROVEN")]
    public void AnInsufficientGuardOrUnsupportedProof_RemainsUnproven(string body, string id)
    {
        Assert.Equal(id, Assert.Single(Analyse(body)).Number.Value);
    }

    [Theory]
    [InlineData("if(input >= 0.0 && input <= 1.0) { float narrowed = (float)input; FloatUnit value = narrowed; }")]
    [InlineData("if(amount >= 0.0m && amount <= 1.0m) { double converted = (double)amount; Unit value = converted; }")]
    public void UnsupportedNumericCasts_DoNotTransferTheirSourceFacts(string body)
    {
        Assert.Equal(["MIST_PA_CAST_HAS_NO_CONVERSION", "MIST_PA_REFINEMENT_UNPROVEN"], Analyse(body).Select(diagnostic => diagnostic.Number.Value).Order());
    }

    [Fact]
    public void AReassignmentToAKnownViolatingValue_IsStillViolated()
    {
        Assert.Equal("MIST_PA_REFINEMENT_VIOLATED", Assert.Single(Analyse("if(input > 0.0) { input = -1.0; Positive value = input; }")).Number.Value);
    }

    private static ImmutableArray<Diagnostic> Analyse(string body)
    {
        var members = $$"""
            {{Declarations}}
            public class RealCases
            {
                public static void Check(double input, double other, double limit, float single, decimal amount,
                    bool flag, Unit unit, PositiveUnit positiveUnit, FloatUnit floatUnit, DecimalUnit decimalUnit)
                {
                    {{body}}
                }
            }
            """;
        var model = StandardModelProbe.Build("", out var buildDiagnostics, members);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var declaring = Assert.IsType<ClassType>(model.TypesByFQN["Probe.RealCases"]);
        var nodes = declaring.MethodsList.SelectMany(method => BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(method.Body))).ToList();
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}