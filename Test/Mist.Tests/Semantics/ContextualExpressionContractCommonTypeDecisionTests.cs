using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ContextualExpressionContractCommonTypeDecisionTests
{
    [Theory]
    [InlineData("Int32", "Int64", "Int64")]
    [InlineData("Int32", "UInt32", "Int64")]
    [InlineData("Byte", "SByte", "Int16")]
    [InlineData("Int16", "UInt16", "Int32")]
    [InlineData("UInt32", "UInt64", "UInt64")]
    [InlineData("Int32", "Single", "Single")]
    [InlineData("Single", "Double", "Double")]
    [InlineData("Int64", "Decimal", "Decimal")]
    public void NumericWideningChoosesTheSameNarrowestTypeInEitherOrder(string left, string right, string expected)
    {
        var model = new HandBuiltModel();
        var a = HandBuiltModel.Literal(model.Type(left));
        var b = HandBuiltModel.Literal(model.Type(right));

        Assert.Same(model.Type(expected), CommonTypeInference.Infer([a, b], primitive => model.Type(primitive.Name)));
        Assert.Same(model.Type(expected), CommonTypeInference.Infer([b, a], primitive => model.Type(primitive.Name)));
    }

    [Fact]
    public void ASharedNominalBaseCanBeInferredWithoutAppearingAmongTheValues()
    {
        var model = new HandBuiltModel();
        var ancestor = HandBuiltModel.Class("Ancestor");
        var parent = HandBuiltModel.Class("Parent", ancestor);
        var left = HandBuiltModel.Literal(HandBuiltModel.Class("Left", parent));
        var right = HandBuiltModel.Literal(HandBuiltModel.Class("Right", parent));

        Assert.Same(parent, CommonTypeInference.Infer([left, right], primitive => model.Type(primitive.Name)));
        Assert.Same(parent, CommonTypeInference.Infer([right, left], primitive => model.Type(primitive.Name)));
    }

    [Fact]
    public void ASharedDeclaredImplicitResultCanBeInferredWithoutAppearingAmongTheValues()
    {
        var model = new HandBuiltModel();
        var target = HandBuiltModel.Class("Target");
        var left = HandBuiltModel.Class("Left");
        var right = HandBuiltModel.Class("Right");
        _ = HandBuiltModel.Conversion(left, left, target);
        _ = HandBuiltModel.Conversion(right, right, target);

        Assert.Same(target, CommonTypeInference.Infer(
            [HandBuiltModel.Literal(left), HandBuiltModel.Literal(right)], primitive => model.Type(primitive.Name)));
    }

    [Fact]
    public void ACommonConstructedBaseKeepsItsSubstitutedArguments()
    {
        var model = new HandBuiltModel();
        var parent = HandBuiltModel.Class("Parent<>");
        var parameter = new TypeParameter("T", 0, parent.Scope, null);
        parent.TypeParametersList.Add(parameter);
        var left = HandBuiltModel.Class("Left<>", new ConstructedType(parent, [parameter]));
        left.TypeParametersList.Add(parameter);
        var right = HandBuiltModel.Class("Right", new ConstructedType(parent, [model.Type("Int32")]));
        var inferred = Assert.IsType<ConstructedType>(CommonTypeInference.Infer(
            [HandBuiltModel.Literal(new ConstructedType(left, [model.Type("Int32")])), HandBuiltModel.Literal(right)],
            primitive => model.Type(primitive.Name)));

        Assert.Same(parent, inferred.Definition);
        Assert.Same(model.Type("Int32"), Assert.Single(inferred.TypeArguments));
    }

    [Theory]
    [InlineData("unrelated-values")]
    [InlineData("numeric-no-unique-target")]
    [InlineData("competing-implicit-targets")]
    [InlineData("competing-nominal-targets")]
    public void IncompatibleOrIncomparableTargetsRemainRefused(string kind)
    {
        var model = new HandBuiltModel();
        IMistType left = HandBuiltModel.Class("Left");
        IMistType right = HandBuiltModel.Class("Right");
        if(kind == "numeric-no-unique-target")
        {
            left = model.Type("Int64");
            right = model.Type("UInt64");
        }
        else if(kind == "competing-implicit-targets")
        {
            foreach(var target in new[] { HandBuiltModel.Class("TargetOne"), HandBuiltModel.Class("TargetTwo") })
            {
                _ = HandBuiltModel.Conversion((ClassType)left, left, target);
                _ = HandBuiltModel.Conversion((ClassType)right, right, target);
            }
        }
        else if(kind == "competing-nominal-targets")
        {
            foreach(var target in new[] { new AbstractType("One", "Probe", new Scope(null), null, null), new AbstractType("Two", "Probe", new Scope(null), null, null) })
            {
                ((ClassType)left).BaseAbstractTypes.Add(target);
                ((ClassType)right).BaseAbstractTypes.Add(target);
            }
        }

        Assert.Null(CommonTypeInference.Infer([HandBuiltModel.Literal(left), HandBuiltModel.Literal(right)], primitive => model.Type(primitive.Name)));
    }

    [Fact]
    public void AnExistingUnionOrUnknownTargetKeepsItsEstablishedConversions()
    {
        var model = new HandBuiltModel();
        var number = HandBuiltModel.Literal(model.Type("Int32"));
        var union = new UnionType([model.Type("Int64"), model.Type("String")]);

        Assert.Same(union, CommonTypeInference.Infer([number, HandBuiltModel.Literal(union)], primitive => model.Type(primitive.Name)));
        Assert.Same(UnknownType.Instance, CommonTypeInference.Infer(
            [number, HandBuiltModel.Literal(UnknownType.Instance)], primitive => model.Type(primitive.Name)));
        Assert.Null(CommonTypeInference.Infer([number, HandBuiltModel.Literal(model.Type("String"))], primitive => model.Type(primitive.Name)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyPositiveFailureIsIgnored(bool reported)
    {
        var model = new HandBuiltModel();
        var failed = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("input", ErrorType.Instance) { IsFailed = reported });
        var inferred = CommonTypeInference.Infer([failed, HandBuiltModel.Literal(model.Type("Int32"))], primitive => model.Type(primitive.Name));

        Assert.Same(reported ? model.Type("Int32") : ErrorType.Instance, inferred);
    }

    [Fact]
    public void TwoDeclaredImplicitOperatorsDoNotBecomeAChainedCommonConversion()
    {
        var model = new HandBuiltModel();
        var first = HandBuiltModel.Class("First");
        var middle = HandBuiltModel.Class("Middle");
        var target = HandBuiltModel.Class("Target");
        var second = HandBuiltModel.Class("Second");
        _ = HandBuiltModel.Conversion(first, first, middle);
        _ = HandBuiltModel.Conversion(middle, middle, target);
        _ = HandBuiltModel.Conversion(second, second, target);

        Assert.Null(CommonTypeInference.Infer([HandBuiltModel.Literal(first), HandBuiltModel.Literal(second)], primitive => model.Type(primitive.Name)));
    }

    [Fact]
    public void AConflictNamesDifferentSoundTypesEvenAfterRepeatedOrFailedInputs()
    {
        var model = new HandBuiltModel();
        var (first, other) = CommonTypeInference.Disagreement(
            [new ErrorExpression(HandBuiltModel.Node()), HandBuiltModel.Literal(model.Type("Int32")),
                HandBuiltModel.Literal(model.Type("Int32")), HandBuiltModel.Literal(model.Type("String"))]);

        Assert.Same(model.Type("Int32"), first);
        Assert.Same(model.Type("String"), other);
    }
}