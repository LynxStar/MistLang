// Tier 1 (Unit): operator types and bound position flags determine refusals without creating a program.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class OperatorPositionRefusalTests
{
    [Theory]
    [InlineData("arithmetic-on-a-class", "MIST0200")]
    [InlineData("arithmetic-on-a-union", "MIST0200")]
    [InlineData("class-equality-without-operator", "MIST0200")]
    [InlineData("comparison-not-applicable", "MIST0200")]
    [InlineData("conditional-access-on-non-optional", "MIST0205")]
    [InlineData("union-concatenated-with-string", "MIST0200")]
    [InlineData("unknown-concatenated-with-string", "MIST0201")]
    [InlineData("unknown-has-no-members", "MIST0202")]
    [InlineData("unknown-has-no-operators", "MIST0201")]
    [InlineData("coalescing-assignment-not-a-statement", "MIST0206")]
    [InlineData("converted-foreach-variable-reassigned", "MIST0207")]
    [InlineData("foreach-variable-reassigned", "MIST0207")]
    [InlineData("readonly-local-coalesced", "MIST0207")]
    [InlineData("readonly-local-reassigned", "MIST0207")]
    [InlineData("readonly-parameter-reassigned", "MIST0207")]
    [InlineData("optional-of-optional", "MIST0213")]
    public void RefusedExpressionKeepsItsDiagnostic(string caseName, string expected)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var text = model.Type("String");
        var dog = HandBuiltModel.Class("Dog");
        dog.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        var union = model.Interner.GetOrCreateUnionType([integer, text]);
        MistDiagnostic? binary(BinaryOperatorKind kind, IMistType left, IMistType right)
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
            "readonly-parameter-reassigned" => PositionDecision.Reassignment(Binding(integer, "parameter", isReadonly: true, ByRefDirection.None)),
            _ => PositionDecision.Reassignment(new LocalVariable("each", caseName == "converted-foreach-variable-reassigned" ? HandBuiltModel.Class("Level") : integer) { IsReadonly = true }),
        };

        Assert.True(diagnostic?.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic?.Number.Value ?? "silence"}");
    }

    [Fact]
    public void AdmittedOperatorsAndPositionsRemainSilent()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");

        Assert.Null(OperatorDecision.Binary(BinaryOperatorKind.Add, integer, integer, type => model.Type(type.Name)));
        Assert.Null(OperatorDecision.Binary(BinaryOperatorKind.Add, ErrorType.Instance, integer, type => model.Type(type.Name)));
        Assert.Null(OperatorDecision.Conditional(model.Interner.GetOrCreateUnionType([integer, model.Type("Void")])));
        Assert.Null(OperatorDecision.Member(integer));
        Assert.Null(PositionDecision.Coalescing(true));
        Assert.Null(PositionDecision.Reassignment(new LocalVariable("mutable", integer)));
        Assert.Null(PositionDecision.Reassignment(Binding(integer, "parameter", isReadonly: false, ByRefDirection.None)));
        Assert.Null(PositionDecision.Optional(integer));
    }

    [Theory]
    [InlineData("local", true, ByRefDirection.None, "local variable")]
    [InlineData("parameter", true, ByRefDirection.None, "parameter")]
    [InlineData("parameter", true, ByRefDirection.In, "parameter")]
    [InlineData("local", false, ByRefDirection.None, null)]
    [InlineData("parameter", false, ByRefDirection.None, null)]
    [InlineData("parameter", false, ByRefDirection.Ref, null)]
    public void AReadonlyBindingIsNamedByWhatItIs(string kind, bool isReadonly, ByRefDirection direction, string? expected)
    {
        var integer = new HandBuiltModel().Type("Int32");

        Assert.Equal(expected, PositionDecision.ReadonlyKind(Binding(integer, kind, isReadonly, direction)));
    }

    private static LocalVariable Binding(IMistType type, string kind, bool isReadonly, ByRefDirection direction)
    {
        var isRef = direction != ByRefDirection.None;
        if(kind == "local")
        {
            return new LocalVariable("total", type) { IsReadonly = isReadonly, IsRef = isRef };
        }

        var modifier = new ParameterModifier(isReadonly, isRef, OwnershipKind.Unrestricted, direction);
        return new LocalVariable("count", type) { IsRef = isRef, Parameter = new Parameter("count", type, modifier) };
    }
}