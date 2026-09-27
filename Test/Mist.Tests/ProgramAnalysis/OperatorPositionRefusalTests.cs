// Tier 1 (Unit): operator types and bound position flags determine refusals without creating a program.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class OperatorPositionRefusalTests
{
    [Theory]
    [InlineData("arithmetic-on-a-class", "MIST_PA_OPERATOR_NOT_APPLICABLE")]
    [InlineData("arithmetic-on-a-union", "MIST_PA_OPERATOR_NOT_APPLICABLE")]
    [InlineData("class-equality-without-operator", "MIST_PA_OPERATOR_NOT_APPLICABLE")]
    [InlineData("comparison-not-applicable", "MIST_PA_OPERATOR_NOT_APPLICABLE")]
    [InlineData("conditional-access-on-non-optional", "MIST_PA_CONDITIONAL_ACCESS_ON_NON_OPTIONAL")]
    [InlineData("union-concatenated-with-string", "MIST_PA_OPERATOR_NOT_APPLICABLE")]
    [InlineData("unknown-concatenated-with-string", "MIST_PA_UNKNOWN_HAS_NO_OPERATORS")]
    [InlineData("unknown-has-no-members", "MIST_PA_UNKNOWN_HAS_NO_MEMBERS")]
    [InlineData("unknown-has-no-operators", "MIST_PA_UNKNOWN_HAS_NO_OPERATORS")]
    [InlineData("coalescing-assignment-not-a-statement", "MIST_PA_COALESCING_ASSIGNMENT_NOT_A_STATEMENT")]
    [InlineData("converted-foreach-variable-reassigned", "MIST_PA_READONLY_LOCAL_REASSIGNED")]
    [InlineData("foreach-variable-reassigned", "MIST_PA_READONLY_LOCAL_REASSIGNED")]
    [InlineData("readonly-local-coalesced", "MIST_PA_READONLY_LOCAL_REASSIGNED")]
    [InlineData("readonly-local-reassigned", "MIST_PA_READONLY_LOCAL_REASSIGNED")]
    [InlineData("optional-of-optional", "MIST_PA_OPTIONAL_OF_OPTIONAL")]
    public void RefusedExpressionKeepsItsDiagnostic(string caseName, string expected)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var text = model.Type("String");
        var dog = HandBuiltModel.Class("Dog");
        dog.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        var union = model.Interner.GetOrCreateUnionType([integer, text]);
        DiagnosticDescriptor? binary(BinaryOperatorKind kind, IMistType left, IMistType right)
        {
            return OperatorDecision.Binary(kind, left, right, type => model.Type(type.Name));
        }

        var diagnostic = caseName switch
        {
            "arithmetic-on-a-class" => binary(BinaryOperatorKind.Add, dog, integer),
            "arithmetic-on-a-union" => binary(BinaryOperatorKind.Add, union, integer),
            "class-equality-without-operator" => binary(BinaryOperatorKind.Equal, dog, dog),
            "comparison-not-applicable" => binary(BinaryOperatorKind.LessThan, text, integer),
            "conditional-access-on-non-optional" => OperatorDecision.Conditional(dog),
            "union-concatenated-with-string" => binary(BinaryOperatorKind.Add, union, text),
            "unknown-concatenated-with-string" => binary(BinaryOperatorKind.Add, UnknownType.Instance, text),
            "unknown-has-no-members" => OperatorDecision.Member(UnknownType.Instance),
            "unknown-has-no-operators" => binary(BinaryOperatorKind.Add, UnknownType.Instance, integer),
            "coalescing-assignment-not-a-statement" => PositionDecision.Coalescing(false),
            "optional-of-optional" => PositionDecision.Optional(model.Interner.GetOrCreateUnionType([integer, model.Type("Void")])),
            _ => PositionDecision.Reassignment(new LocalVariable("each", caseName == "converted-foreach-variable-reassigned" ? HandBuiltModel.Class("Level") : integer) { IsReadonly = true }),
        };

        Assert.True(diagnostic?.Id.Value == expected, $"{caseName}: expected {expected}, got {diagnostic?.Id.Value ?? "silence"}");
    }

    [Fact]
    public void AdmittedOperatorsAndPositionsRemainSilent()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");

        Assert.Null(OperatorDecision.Binary(BinaryOperatorKind.Add, integer, integer, type => model.Type(type.Name)));
        Assert.Null(OperatorDecision.Binary(BinaryOperatorKind.Add, ErrorType.Instance, integer, type => model.Type(type.Name)));
        Assert.Null(OperatorDecision.Conditional(model.Interner.GetOrCreateUnionType([integer, model.Type("Void")])));
        Assert.Null(PositionDecision.Coalescing(true));
        Assert.Null(PositionDecision.Reassignment(new LocalVariable("mutable", integer)));
        Assert.Null(PositionDecision.Optional(integer));
    }
}