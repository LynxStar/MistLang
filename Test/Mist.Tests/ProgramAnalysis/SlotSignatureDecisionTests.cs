using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1: SlotSignatures, the one comparison of a slot with a member that might answer it, over methods,
// indexers and types constructed directly. A slot is declared on the abstract IShape and a filler on the
// class Thing, so each type parameter belongs to the declaration a real one would.
[Trait("Tier", "1")]
public sealed class SlotSignatureDecisionTests
{
    private readonly HandBuiltModel built = new();
    private readonly AbstractType shape = new("IShape", "Probe", new Scope(null), null, null);
    private readonly ClassType thing = HandBuiltModel.Class("Thing");

    private IMistType Int => built.Type("Int32");

    private IMistType Text => built.Type("String");

    [Theory]
    [InlineData(ByRefDirection.Ref, ByRefDirection.Ref, true)]
    [InlineData(ByRefDirection.In, ByRefDirection.In, true)]
    [InlineData(ByRefDirection.None, ByRefDirection.None, true)]
    [InlineData(ByRefDirection.Ref, ByRefDirection.None, false)]
    [InlineData(ByRefDirection.None, ByRefDirection.Ref, false)]
    [InlineData(ByRefDirection.In, ByRefDirection.Ref, false)]
    [InlineData(ByRefDirection.Ref, ByRefDirection.In, false)]
    public void AParameterAnswersOnlyWhenPassedTheSameWay(ByRefDirection slotPassing, ByRefDirection fillerPassing, bool answers)
    {
        var slot = Declare(shape, "Act", Int, [], Passed("x", Int, slotPassing));
        var filler = Declare(thing, "Act", Int, [], Passed("y", Int, fillerPassing));

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, built.Interner));
    }

    [Theory]
    [InlineData(OwnershipKind.Owned, OwnershipKind.Unrestricted)]
    [InlineData(OwnershipKind.Borrowed, OwnershipKind.Frozen)]
    public void AParametersOwnershipIsNoPartOfHowItIsPassed(OwnershipKind slotTier, OwnershipKind fillerTier)
    {
        var slot = Declare(shape, "Act", Int, [], new Parameter("x", Int, new ParameterModifier(false, false, slotTier)));
        var filler = Declare(thing, "Act", Int, [], new Parameter("x", Int, new ParameterModifier(false, false, fillerTier)));

        Assert.True(SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, built.Interner));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void ARefReturnAnswersOnlyARefReturn(bool slotRef, bool fillerRef, bool answers)
    {
        var slot = Declare(shape, "Get", Int, [], modifiers: DeclarationFragments.Public with { IsRef = slotRef });
        var filler = Declare(thing, "Get", Int, [], modifiers: DeclarationFragments.Public with { IsRef = fillerRef });

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, built.Interner));
    }

    [Theory]
    [InlineData("T", "T", "U", "U", true)]
    [InlineData("T", "T", "Int32", "Int32", false)]
    [InlineData("T", "T", "U", "Int32", false)]
    [InlineData("Int32", "T", "Int32", "U", true)]
    [InlineData("Int32", "Int32", "U", "U", false)]
    [InlineData("List<T>", "T", "List<U>", "U", true)]
    [InlineData("List<T>", "T", "List<Int32>", "Int32", false)]
    [InlineData("(T, T)", "T", "(U, U)", "U", true)]
    [InlineData("(T, T)", "T", "(Int32, Int32)", "Int32", false)]
    [InlineData("T?", "T", "U?", "U", true)]
    [InlineData("T?", "T", "Int32?", "Int32", false)]
    [InlineData("func<T>", "T", "func<U>", "U", true)]
    [InlineData("func<T>", "T", "func<Int32>", "Int32", false)]
    public void AGenericSlotIsAnsweredByTheFillersOwnParameterInEveryPosition(
        string slotReturns, string slotTakes, string fillerReturns, string fillerTakes, bool answers)
    {
        var t = Owned("T", shape);
        var u = Owned("U", thing);
        var slot = Declare(shape, "Identity", Spelled(slotReturns, t), [t], new Parameter("x", Spelled(slotTakes, t)));
        var filler = Declare(thing, "Identity", Spelled(fillerReturns, u), [u], new Parameter("x", Spelled(fillerTakes, u)));

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, built.Interner));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void TwoMethodTypeParametersCorrespondByPosition(int returned, bool answers)
    {
        var (a, b) = (Owned("A", shape, 0), Owned("B", shape, 1));
        var (x, y) = (Owned("A", thing, 0), Owned("B", thing, 1));
        var slot = Declare(shape, "First", a, [a, b], new Parameter("a", a), new Parameter("b", b));
        var filler = Declare(thing, "First", returned == 0 ? x : y, [x, y], new Parameter("a", x), new Parameter("b", y));

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, built.Interner));
    }

    [Fact]
    public void AGenericSlotIsNotAnsweredByAMemberWithAnotherCountOfTypeParameters()
    {
        var t = Owned("T", shape);
        var slot = Declare(shape, "Make", t, [t]);
        var filler = Declare(thing, "Make", Int, []);

        Assert.False(SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, built.Interner));
    }

    [Theory]
    [InlineData("Int32", true)]
    [InlineData("String", false)]
    public void AnAbstractsTypeArgumentClosesTheSlot(string filled, bool answers)
    {
        var t = Owned("T", shape);
        var slot = Declare(shape, "Get", t, []);
        var filler = Declare(thing, "Get", built.Type(filled), []);

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.ForParameters([t], [Int]), built.Interner));
    }

    [Theory]
    [InlineData("Int32", true)]
    [InlineData("String", false)]
    public void AnInheritedFillerIsReadThroughTheBaseItCameFrom(string baseArgument, bool answers)
    {
        var held = Owned("U", HandBuiltModel.Class("Base"));
        var slot = Declare(shape, "Get", Int, []);
        var filler = Declare(thing, "Get", held, []);
        var throughBase = TypeSubstitution.ForParameters([held], [built.Type(baseArgument)]);

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, throughBase, built.Interner));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AConformersOwnTypeParameterAgreesOnlyWithItself(bool returnsTheOther, bool answers)
    {
        var abstractParameter = Owned("T", shape);
        var (t, u) = (Owned("T", thing, 0), Owned("U", thing, 1));
        var slot = Declare(shape, "Get", abstractParameter, []);
        var filler = Declare(thing, "Get", returnsTheOther ? u : t, []);

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.ForParameters([abstractParameter], [t]), built.Interner));
    }

    [Fact]
    public void AnAbstractsParameterTheConformerGaveNoArgumentFor_AgreesWithAnything()
    {
        Assert.True(SlotSignatures.Agree(Owned("T", shape), Int));
        Assert.True(SlotSignatures.Agree(Text, Owned("T", shape)));
    }

    [Fact]
    public void AnAssociatedTypeSlot_AgreesWithTheConformersNestedType()
    {
        var kind = new TypeSlot("Kind", 0, shape.Scope, shape);

        Assert.True(SlotSignatures.Agree(kind, HandBuiltModel.Class("Kind")));
    }

    [Fact]
    public void AnErrorOnEitherSide_AgreesSoNothingCascades()
    {
        Assert.True(SlotSignatures.Agree(ErrorType.Instance, Int));
        Assert.True(SlotSignatures.Agree(Int, ErrorType.Instance));
    }

    [Fact]
    public void UnknownIsATypeAndAgreesOnlyWithItself()
    {
        Assert.True(SlotSignatures.Agree(UnknownType.Instance, UnknownType.Instance));
        Assert.False(SlotSignatures.Agree(UnknownType.Instance, Int));
    }

    [Theory]
    [InlineData("Int32", "Int32", true)]
    [InlineData("String", "Int32", false)]
    [InlineData("Int32", "String", false)]
    public void AnIndexerIsReadThroughBothSubstitutions(string inheritedType, string inheritedParameter, bool answers)
    {
        var (t, k) = (Owned("T", shape, 0), Owned("K", shape, 1));
        var (u, j) = (Owned("U", thing, 0), Owned("J", thing, 1));
        var slot = new Indexer(t, [new Parameter("key", k)], DeclarationFragments.Public);
        var filler = new Indexer(u, [new Parameter("key", j)], DeclarationFragments.Public);
        var slotClosing = TypeSubstitution.ForParameters([t, k], [Int, Int]);
        var throughBase = TypeSubstitution.ForParameters([u, j], [built.Type(inheritedType), built.Type(inheritedParameter)]);

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, slotClosing, throughBase, built.Interner));
    }

    [Theory]
    [InlineData(ByRefDirection.None, false, true)]
    [InlineData(ByRefDirection.Ref, false, false)]
    [InlineData(ByRefDirection.None, true, false)]
    public void AnIndexerAnswersOnlyWithTheSamePassingAndReturn(ByRefDirection fillerPassing, bool fillerRef, bool answers)
    {
        var slot = new Indexer(Int, [new Parameter("i", Int)], DeclarationFragments.Public);
        var filler = new Indexer(Int, [Passed("i", Int, fillerPassing)], DeclarationFragments.Public with { IsRef = fillerRef });

        Assert.Equal(answers, SlotSignatures.Answers(slot, filler, TypeSubstitution.Empty, TypeSubstitution.Empty, built.Interner));
    }

    [Fact]
    public void ASlotIsDisplayedWithItsTypeParametersAndPassing()
    {
        var t = Owned("T", shape);
        var slot = Declare(shape, "Act", t, [t], Passed("x", t, ByRefDirection.Ref), Passed("y", Int, ByRefDirection.In));
        var indexer = new Indexer(Int, [Passed("i", Int, ByRefDirection.Ref)], DeclarationFragments.Public with { IsRef = true });

        Assert.Equal("T Act<T>(ref T, readonly ref int)", SlotSignatures.Display(slot, TypeSubstitution.Empty, built.Interner));
        Assert.Equal("ref int this[ref int]", SlotSignatures.Display(indexer, TypeSubstitution.Empty, built.Interner));
    }

    private static Method Declare(
        MemberType owner, string name, IMistType returns, TypeParameter[] typeParameters, params Parameter[] parameters)
    {
        return Declare(owner, name, returns, typeParameters, DeclarationFragments.Public, parameters);
    }

    private static Method Declare(
        MemberType owner, string name, IMistType returns, TypeParameter[] typeParameters, MemberModifiers modifiers, params Parameter[] parameters)
    {
        var method = new Method(name, returns, [.. parameters], modifiers) { DeclaringType = owner };
        method.TypeParametersList.AddRange(typeParameters);
        return method;
    }

    private static TypeParameter Owned(string name, MistType owner, int ordinal = 0)
    {
        return new TypeParameter(name, ordinal, new Scope(null), owner);
    }

    private static Parameter Passed(string name, IMistType type, ByRefDirection passing)
    {
        var isRef = passing != ByRefDirection.None;
        return new Parameter(name, type, new ParameterModifier(passing == ByRefDirection.In, isRef, OwnershipKind.Unrestricted, passing));
    }

    private IMistType Spelled(string spelling, TypeParameter parameter)
    {
        var interner = built.Interner;
        var element = spelling.Contains("Int32", StringComparison.Ordinal) ? Int : parameter;

        return spelling switch
        {
            _ when spelling.StartsWith("List<", StringComparison.Ordinal) => interner.GetOrCreateConstructedType(built.Type("List<>"), [element]),
            _ when spelling.StartsWith('(') => interner.GetOrCreateTupleType([element, element]),
            _ when spelling.EndsWith('?') => interner.GetOrCreateUnionType([element, built.Type("None")]),
            _ when spelling.StartsWith("func<", StringComparison.Ordinal) => interner.GetOrCreateFunctionType([], element),
            _ => element,
        };
    }
}