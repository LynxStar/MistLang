using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ClosedOperatorSelectionTests
{
    private readonly HandBuiltModel built = new();

    private ClassType Integer => built.Type("Int32");

    private ClassType Text => built.Type("String");

    [Fact]
    public void AGenericOwnersOperator_IsClosedByTheOperandsConstruction()
    {
        var (tag, held) = Generic("Tag");
        var open = Of(tag, held);
        var plus = Declare(tag, OperatorKind.Plus, held, open, open);
        var closed = Of(tag, Integer);

        var (selected, tied) = OperatorSelection.Select(OperatorKind.Plus, [closed, closed], null, built.Interner);

        Assert.Empty(tied);
        Assert.NotNull(selected);
        Assert.Same(plus, selected.Declaration);
        Assert.Same(closed, selected.Owner);
        Assert.All(selected.ParameterTypes, parameter => Assert.Same(closed, parameter));
        Assert.Same(Integer, selected.ReturnType);
        Assert.Same(Integer, selected.Closing.Apply(held, built.Interner));
    }

    [Fact]
    public void AGenericOwnersUnaryOperator_AnswersItsClosedReturn()
    {
        var (tag, held) = Generic("Tag");
        var open = Of(tag, held);
        var negate = Declare(tag, OperatorKind.Minus, open, open);
        var closed = Of(tag, Integer);

        var choice = OperatorAdmissibility.Unary(UnaryOperatorKind.Negate, closed, built.Interner);

        Assert.Same(negate, choice.Selected?.Declaration);
        Assert.Same(closed, choice.Type);
    }

    [Fact]
    public void EachInstantiationClosesOnlyItsOwnCandidates()
    {
        var (tag, held) = Generic("Tag");
        var open = Of(tag, held);
        _ = Declare(tag, OperatorKind.Plus, held, open, open);
        var number = Of(tag, Integer);
        var word = Of(tag, Text);

        var mixed = Binary(number, word);

        Assert.Null(mixed.Type);
        Assert.Null(mixed.Selected);
        Assert.Empty(mixed.Tied);
        Assert.Same(Text, Binary(word, word).Type);
        Assert.Same(Integer, Binary(number, number).Type);
    }

    [Fact]
    public void AnOperatorOfAConstructedBase_IsReachedThroughTheDerivedOperand()
    {
        var (baseType, value) = Generic("Base");
        var negate = Declare(baseType, OperatorKind.Minus, value, Of(baseType, value));
        var closedBase = Of(baseType, Integer);
        var derived = HandBuiltModel.Class("Derived", closedBase);

        var choice = OperatorAdmissibility.Unary(UnaryOperatorKind.Negate, derived, built.Interner);

        Assert.Same(negate, choice.Selected?.Declaration);
        Assert.Same(Integer, choice.Type);
        Assert.Same(closedBase, choice.Selected?.Owner);
        Assert.Same(closedBase, Assert.Single(choice.Selected!.ParameterTypes));
    }

    [Fact]
    public void TwoConstructedBaseClauses_CloseARenamedParameterAtEachLevel()
    {
        var (root, rootParameter) = Generic("Root");
        var negate = Declare(root, OperatorKind.Minus, rootParameter, Of(root, rootParameter));
        var times = Declare(root, OperatorKind.Star, rootParameter, Of(root, rootParameter), Of(root, rootParameter));
        var (middle, middleParameter) = Generic("Middle", "U");
        middle.BaseType = Of(root, middleParameter);
        var leaf = HandBuiltModel.Class("Leaf", Of(middle, Integer));

        var unary = OperatorAdmissibility.Unary(UnaryOperatorKind.Negate, leaf, built.Interner);
        var binary = Binary(leaf, leaf, BinaryOperatorKind.Multiply);

        Assert.Same(negate, unary.Selected?.Declaration);
        Assert.Same(Integer, unary.Type);
        Assert.Same(Integer, unary.Selected!.Closing.Apply(rootParameter, built.Interner));
        Assert.Same(Integer, unary.Selected.Closing.Apply(middleParameter, built.Interner));
        Assert.Same(times, binary.Selected?.Declaration);
        Assert.Same(Integer, binary.Type);
    }

    [Fact]
    public void AnOwnerParameterUsedInSeveralPositions_IsClosedInEach()
    {
        var pair = HandBuiltModel.Class("Pair");
        var first = new TypeParameter("A", 0, pair.Scope, pair);
        var second = new TypeParameter("B", 1, pair.Scope, pair);
        pair.TypeParametersList.AddRange([first, second]);
        _ = Declare(pair, OperatorKind.Plus, first, Of(pair, first, second), Of(pair, second, first));
        var numberFirst = Of(pair, Integer, Text);
        var wordFirst = Of(pair, Text, Integer);

        Assert.Same(Integer, Binary(numberFirst, wordFirst).Type);
        Assert.Same(Text, Binary(wordFirst, numberFirst).Type);
        Assert.Null(Binary(numberFirst, numberFirst).Type);
    }

    [Fact]
    public void EquallyApplicableOperatorsOfTwoOwners_TieAndAreDescribedByOwner()
    {
        var left = HandBuiltModel.Class("Left");
        var right = HandBuiltModel.Class("Right");
        var fromLeft = Declare(left, OperatorKind.Plus, Text, left, right);
        var fromRight = Declare(right, OperatorKind.Plus, Text, left, right);

        var choice = Binary(left, right);

        Assert.Null(choice.Type);
        Assert.Null(choice.Selected);
        Assert.Equal(new[] { fromLeft, fromRight }, choice.Tied.Select(candidate => candidate.Declaration));
        Assert.Equal(
            new[] { "Left.operator +(Left, Right)", "Right.operator +(Left, Right)" },
            choice.Tied.Select(OperatorSelection.Describe));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AnExactParameter_BeatsAWiderOneWhateverTheOrderOrOwner(bool exactFirst, bool exactOnTheOperand)
    {
        var money = HandBuiltModel.Struct("Money");
        var wide = HandBuiltModel.Class("Wide");
        var narrow = HandBuiltModel.Class("Narrow", wide);
        var exactOwner = exactOnTheOperand ? (MemberType)narrow : money;
        Operator exact;
        if(exactFirst)
        {
            exact = Declare(exactOwner, OperatorKind.Plus, Integer, money, narrow);
            _ = Declare(money, OperatorKind.Plus, Text, money, wide);
        }
        else
        {
            _ = Declare(money, OperatorKind.Plus, Text, money, wide);
            exact = Declare(exactOwner, OperatorKind.Plus, Integer, money, narrow);
        }

        var choice = Binary(money, narrow);

        Assert.Same(exact, choice.Selected?.Declaration);
        Assert.Same(Integer, choice.Type);
    }

    [Theory]
    [InlineData(1L, "Byte")]
    [InlineData(300L, "Int64")]
    [InlineData(null, "Int64")]
    public void AnIntegerConstant_ReachesANarrowerDeclaredParameterItFits(long? constant, string selected)
    {
        var money = HandBuiltModel.Struct("Money");
        _ = Declare(money, OperatorKind.Plus, Text, money, built.Type("Int64"));
        _ = Declare(money, OperatorKind.Plus, Text, money, built.Type("Byte"));

        var choice = Binary(money, Integer, constants: [null, constant]);

        Assert.Equal(selected, choice.Selected?.ParameterTypes[1].Name);
    }

    [Theory]
    [InlineData("SByte", "Byte", "Int32", true)]
    [InlineData("Int16", "UInt32", "Byte", false)]
    public void ASignedAndAnUnsignedTarget_Tie(string signedTarget, string unsignedTarget, string operand, bool constant)
    {
        var money = HandBuiltModel.Struct("Money");
        _ = Declare(money, OperatorKind.Plus, Text, money, built.Type(signedTarget));
        _ = Declare(money, OperatorKind.Plus, Text, money, built.Type(unsignedTarget));

        var choice = Binary(money, built.Type(operand), constants: [null, constant ? 1 : null]);

        Assert.Null(choice.Selected);
        Assert.Equal(2, choice.Tied.Count);
    }

    [Theory]
    [InlineData("Int32", true)]
    [InlineData("String", false)]
    public void AParameterFixedAtTheDeclaration_BeatsOneTheOwnerSupplies(string argument, bool fixedWins)
    {
        var (keyed, key) = Generic("Keyed", "K");
        var byKey = Declare(keyed, OperatorKind.Plus, Text, Of(keyed, key), key);
        var byInteger = Declare(keyed, OperatorKind.Plus, Text, Of(keyed, key), Integer);
        var operand = built.Type(argument);

        var choice = Binary(Of(keyed, operand), operand);

        Assert.Same(fixedWins ? byInteger : byKey, choice.Selected?.Declaration);
    }

    [Fact]
    public void ANarrowerReferenceParameter_BeatsAWiderOne()
    {
        var money = HandBuiltModel.Struct("Money");
        var animal = HandBuiltModel.Class("Animal");
        var dog = HandBuiltModel.Class("Dog", animal);
        _ = Declare(money, OperatorKind.Plus, Text, money, animal);
        var forDogs = Declare(money, OperatorKind.Plus, Text, money, dog);

        Assert.Same(forDogs, Binary(money, HandBuiltModel.Class("Puppy", dog)).Selected?.Declaration);
    }

    [Theory]
    [InlineData(1L, "Int32")]
    [InlineData(null, "Byte")]
    public void AnIntrinsicOperator_TakesAConstantAtItsOwnType(long? constant, string selected)
    {
        var small = built.Type("Byte");
        _ = Declare(small, OperatorKind.Plus, small, intrinsic: true, small, small);
        _ = Declare(Integer, OperatorKind.Plus, Integer, intrinsic: true, Integer, Integer);
        var right = constant is null ? small : Integer;

        var choice = Binary(small, right, constants: [null, constant]);

        Assert.Equal(selected, choice.Type?.Name);
    }

    [Theory]
    [InlineData("refined")]
    [InlineData("alias")]
    public void ATransparentWrapper_IsClosedThroughWhatItWraps(string wrapper)
    {
        var (tag, held) = Generic("Tag");
        var open = Of(tag, held);
        _ = Declare(tag, OperatorKind.Plus, held, open, open);
        var closed = Of(tag, Integer);
        IMistType operand = wrapper == "refined"
            ? HandBuiltModel.Refined(closed, "Checked")
            : new TypeAlias("Labeled", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = closed };

        var choice = Binary(operand, operand);

        Assert.Same(Integer, choice.Type);
        Assert.All(choice.Selected!.ParameterTypes, parameter => Assert.Same(closed, parameter));
    }

    [Fact]
    public void ADeclaredImplicitConversion_ReachesTheClosedParameter()
    {
        var feet = HandBuiltModel.Class("Feet");
        var meters = HandBuiltModel.Class("Meters");
        _ = HandBuiltModel.Conversion(meters, meters, feet);
        var plus = Declare(feet, OperatorKind.Plus, feet, feet, feet);

        Assert.Same(plus, Binary(feet, meters).Selected?.Declaration);
    }

    [Fact]
    public void ANonGenericOwner_ReadsItsDeclarationUnchanged()
    {
        var vec = HandBuiltModel.Struct("Vec");
        var plus = Declare(vec, OperatorKind.Plus, vec, vec, vec);

        var selected = Binary(vec, vec).Selected;

        Assert.Same(plus, selected?.Declaration);
        Assert.Same(vec, selected!.ReturnType);
        Assert.Equal(new IMistType[] { vec, vec }, selected.ParameterTypes);
    }

    [Fact]
    public void NoApplicableOperator_LeavesNeitherAChoiceNorATie()
    {
        var money = HandBuiltModel.Struct("Money");
        _ = Declare(money, OperatorKind.Plus, money, money, money);

        var choice = Binary(money, Integer);

        Assert.Null(choice.Type);
        Assert.Null(choice.Selected);
        Assert.Empty(choice.Tied);
    }

    private OperatorChoice Binary(
        IMistType left, IMistType right, BinaryOperatorKind kind = BinaryOperatorKind.Add, IReadOnlyList<Int128?>? constants = null)
    {
        return OperatorAdmissibility.Binary(kind, left, right, wellKnown => built.Type(wellKnown.Name), constants, built.Interner);
    }

    private ConstructedType Of(ClassType definition, params IMistType[] arguments)
    {
        return built.Interner.GetOrCreateConstructedType(definition, arguments);
    }

    private static (ClassType Owner, TypeParameter Parameter) Generic(string name, string parameter = "T", MistType? baseType = null)
    {
        var owner = HandBuiltModel.Class(name, baseType);
        var typeParameter = new TypeParameter(parameter, 0, owner.Scope, owner);
        owner.TypeParametersList.Add(typeParameter);
        return (owner, typeParameter);
    }

    private static Operator Declare(MemberType owner, OperatorKind kind, IMistType returns, params IMistType[] parameters)
    {
        return Declare(owner, kind, returns, intrinsic: false, parameters);
    }

    private static Operator Declare(MemberType owner, OperatorKind kind, IMistType returns, bool intrinsic, params IMistType[] parameters)
    {
        var declared = new Operator(
            kind, returns, [.. parameters.Select((type, i) => new Parameter($"p{i}", type))],
            new MemberModifiers { IsStatic = true, IsIntrinsic = intrinsic })
        {
            DeclaringType = owner,
        };
        owner.OperatorsList.Add(declared);
        return declared;
    }
}