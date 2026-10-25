using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1: decide the source and clone argument conversions directly on hand-built types.
[Trait("Tier", "1")]
public sealed class NewtypeCloneAssignabilityTests
{
    [Theory]
    [InlineData("newtype-clone-passes-itself-as-its-source", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    public void CloneReceiverCannotStandInForItsSource(string refusal, string diagnosticId)
    {
        var built = new HandBuiltModel();
        var lion = HandBuiltModel.Class("Lion");
        var puma = HandBuiltModel.Class("Puma");
        puma.CloneSource = lion;
        puma.CloneSourceType = lion;
        var parameter = new Parameter("lion", lion);
        var decision = Assignability.Judge(new ThisExpression(HandBuiltModel.Node(40), puma), parameter.Type, built.Interner);
        Assert.NotNull(decision);
        Assert.True(decision.Number.Value == diagnosticId, refusal);
        Assert.Null(Assignability.Judge(new ThisExpression(HandBuiltModel.Node(40), lion), parameter.Type, built.Interner));
    }

    // `type Kept = new Box; type Again = new Kept; type Other = new Box;` -- Again's Source_Type is Kept,
    // the type its declaration names, while the declaration it is bound from stays Box's.
    [Fact]
    public void ACloneOfACloneLeavesForItsImmediateSource_AndReachesTheRootOnlyThroughThatCopy()
    {
        var chain = new Chain();

        Assert.True(ConversionRelation.IsWithin(chain.Again, chain.Kept));
        Assert.False(ConversionRelation.IsWithin(chain.Again, chain.Box));
        Assert.True(ConversionRelation.CopiesAClone(chain.Again, chain.Box));
        Assert.True(ConversionRelation.CopiesAClone(chain.Again, chain.Kept));
        Assert.False(ConversionRelation.IsWithinAsItself(chain.Again, chain.Kept));
        Assert.False(ConversionRelation.IsWithin(chain.Again, chain.Other));
    }

    [Fact]
    public void ALeaveToTheRoot_IsRecordedAsTheCopyToTheImmediateSource()
    {
        var chain = new Chain();

        var called = Assert.Single(CastOperatorSelection.Implicit(chain.Again, chain.Box, chain.Built.Interner));

        Assert.True(called.Operator.CopiesClone);
        Assert.Same(chain.Kept, called.Result);
        Assert.Empty(CastOperatorSelection.Implicit(chain.Again, chain.Kept, chain.Built.Interner));
    }

    [Theory]
    [InlineData("Again", "Kept", null)]
    [InlineData("Again", "Box", null)]
    [InlineData("Kept", "Again", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Box", "Again", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Other", "Kept", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Again", "Other", "MIST_PA_CAST_REQUIRED")]
    public void AValueEntersAChainedCloneOnlyFromItsImmediateSource(string from, string to, string? expected)
    {
        var chain = new Chain();

        Assert.Equal(expected, Assignability.Judge(chain[from], chain[to], chain.Built.Interner)?.Number.Value);
    }

    [Theory]
    [InlineData("Kept", "Again", "Explicit")]
    [InlineData("Box", "Again", "None")]
    [InlineData("Other", "Kept", "Explicit")]
    [InlineData("Again", "Other", "Explicit")]
    public void ACastBetweenSiblingsAndAlongTheChain_IsClassifiedThroughEachImmediateSource(string from, string to, string expected)
    {
        var chain = new Chain();

        Assert.Equal(expected, ConversionClassifier.Classify(chain[from], chain[to], chain.Built.Interner).ToString());
    }

    // `type Age = new int; type Years = new Age;` -- `Years y = 30;` holds because `Age a = 30;` does
    // (newtypes spec 9.1), and enters through each newtype's own conversion in turn.
    [Fact]
    public void ALiteralEntersACloneOfACloneThroughTheCastIntoEachSource()
    {
        var built = new HandBuiltModel();
        var integer = built.Type("Int32");
        var age = Clone("Age", integer, integer);
        var years = Clone("Years", integer, age);
        var literal = HandBuiltModel.Literal(integer, 30L);

        var admitted = Assert.IsType<CastExpression>(NewtypeLiterals.Admit(literal, years, built.Interner));
        var entered = Assert.IsType<CastExpression>(admitted.Operand);

        Assert.Same(years, admitted.Type);
        Assert.True(admitted.Operator is { CopiesClone: true, IsImplicit: false });
        Assert.Same(age, entered.Type);
        Assert.Same(literal, entered.Operand);
    }

    [Fact]
    public void ALiteralTheRootCannotHold_EntersNoLinkOfTheChain()
    {
        var built = new HandBuiltModel();
        var small = built.Type("Byte");
        var tiny = Clone("Tiny", small, small);
        var tinier = Clone("Tinier", small, tiny);
        var literal = HandBuiltModel.Literal(built.Type("Int32"), 300L);

        Assert.Same(literal, NewtypeLiterals.Admit(literal, tinier, built.Interner));
    }

    [Fact]
    public void AnOptionalTupleOrNestedAggregateOfAClone_IsNeverTheSourcesAsItself()
    {
        var built = new HandBuiltModel();
        var (point, position) = Copyable(HandBuiltModel.Struct("Point"), HandBuiltModel.Struct("Position"));
        var text = built.Type("String");
        var integer = built.Type("Int32");
        var nothing = built.Type("Void");
        var box = HandBuiltModel.Class("Box");
        var parameter = new TypeParameter("T", 0, box.Scope, box);
        box.TypeParametersList.Add(parameter);
        var boxOfInt = built.Interner.GetOrCreateConstructedType(box, [integer]);
        var intBox = Clone("IntBox", box, boxOfInt);

        IMistType optional(IMistType type)
        {
            return built.Interner.GetOrCreateUnionType([type, nothing]);
        }

        var pairs = new (IMistType Clone, IMistType Source)[]
        {
            (optional(position), optional(point)),
            (built.Interner.GetOrCreateTupleType([position, integer]), built.Interner.GetOrCreateTupleType([point, integer])),
            (built.Interner.GetOrCreateTupleType([optional(position), text]), built.Interner.GetOrCreateTupleType([optional(point), text])),
            (optional(intBox), optional(boxOfInt)),
        };

        foreach(var (clone, source) in pairs)
        {
            Assert.True(ConversionRelation.IsWithin(clone, source), $"{clone.FullyQualifiedName} leaves for {source.FullyQualifiedName}");
            Assert.False(ConversionRelation.IsWithinAsItself(clone, source), $"{clone.FullyQualifiedName} is not {source.FullyQualifiedName}");
            Assert.True(ConversionRelation.CopiesAClone(clone, source), $"{clone.FullyQualifiedName} reaches {source.FullyQualifiedName} by a copy");
            Assert.True(ConversionRelation.IsWithinAsItself(source, source));
        }
    }

    [Fact]
    public void AnExtensionForASourcesAggregate_ReachesTheSourcesAggregateAndNotTheClones()
    {
        var built = new HandBuiltModel();
        var (point, position) = Copyable(HandBuiltModel.Struct("Point"), HandBuiltModel.Struct("Position"));
        var text = built.Type("String");
        var nothing = built.Type("Void");
        var optionalPoint = built.Interner.GetOrCreateUnionType([point, nothing]);
        var optionalPosition = built.Interner.GetOrCreateUnionType([position, nothing]);
        var nested = built.Interner.GetOrCreateTupleType([optionalPoint, text]);
        var nestedClone = built.Interner.GetOrCreateTupleType([optionalPosition, text]);

        Extend(built, optionalPoint, "Label");
        Extend(built, nested, "Pairs");

        _ = Assert.IsType<MethodGroupResult>(Find(built, optionalPoint, "Label"));
        _ = Assert.IsType<MethodGroupResult>(Find(built, nested, "Pairs"));
        _ = Assert.IsType<NotFoundResult>(Find(built, optionalPosition, "Label"));
        _ = Assert.IsType<NotFoundResult>(Find(built, nestedClone, "Pairs"));
    }

    // `with Money : IPriced { ... }` is Money's alone (newtypes spec 3.5), so `IPriced p = cash;` copies
    // out to Money first; C# admits no user-defined conversion into an interface, so the copy is recorded.
    [Fact]
    public void ACopyIntoAnAbstractOnlyTheSourceConformsTo_IsRecordedRatherThanContained()
    {
        var built = new HandBuiltModel();
        var (money, cash) = Copyable(HandBuiltModel.Class("Money"), HandBuiltModel.Class("Cash"));
        var priced = new AbstractType("IPriced", "Probe", new Scope(null), null, null);
        var optionalPriced = built.Interner.GetOrCreateUnionType([priced, built.Type("Void")]);
        money.ConformancesList.Add(new ConformanceDeclaration(money, priced, "Probe", [], [], HandBuiltModel.Node()));

        Assert.True(ConversionRelation.IsWithin(money, priced));
        Assert.False(ConversionRelation.IsWithin(cash, priced));
        Assert.False(ConversionRelation.IsWithin(cash, optionalPriced));
        Assert.False(ConversionRelation.IsWithinAsItself(cash, priced));
        Assert.True(ConversionRelation.CopiesAClone(cash, priced));

        foreach(var target in new IMistType[] { priced, optionalPriced })
        {
            var called = Assert.Single(CastOperatorSelection.Implicit(cash, target, built.Interner));
            Assert.True(called.Operator is { CopiesClone: true, IsImplicit: true });
            Assert.Same(money, called.Result);
            Assert.Null(Assignability.Judge(cash, target, built.Interner));
        }
    }

    [Fact]
    public void AnAbstractTheClonesOwnDeclarationNames_IsContainedWithoutACopy()
    {
        var built = new HandBuiltModel();
        var (money, cash) = Copyable(HandBuiltModel.Class("Money"), HandBuiltModel.Class("Cash"));
        var striped = new AbstractType("IStriped", "Probe", new Scope(null), null, null);
        money.BaseAbstractTypes.Add(striped);
        cash.BaseAbstractTypes.Add(striped);

        Assert.True(ConversionRelation.IsWithinAsItself(cash, striped));
        Assert.Empty(CastOperatorSelection.Implicit(cash, striped, built.Interner));
    }

    private static (T Source, T Clone) Copyable<T>(T source, T clone)
        where T : ConcreteType
    {
        AddCopies(clone, source, source);
        return (source, clone);
    }

    private static ClassType Clone(string name, ConcreteType root, IMistType source)
    {
        var clone = HandBuiltModel.Class(name);
        AddCopies(clone, root, source);
        return clone;
    }

    private static void AddCopies(ConcreteType clone, ConcreteType root, IMistType source)
    {
        clone.CloneSource = root;
        clone.CloneSourceType = source;
        clone.CastOperatorsList.Add(new CastOperator(source, new Parameter("value", clone), true, default) { CopiesClone = true, DeclaringType = clone });
        clone.CastOperatorsList.Add(new CastOperator(clone, new Parameter("value", source), false, default) { CopiesClone = true, DeclaringType = clone });
    }

    private static void Extend(HandBuiltModel built, IMistType receiver, string name)
    {
        var block = new ExtensionBlock("self", "", new Scope(null), null) { ResolvedReceiverType = receiver };
        block.MethodsList.Add(new Method(name, built.Type("Int32"), [], default));
        built.Model.GetOrAddNamespace("").Extensions[receiver] = [block];
    }

    private static LookupResult Find(HandBuiltModel built, IMistType receiver, string name)
    {
        return ExtensionLookup.Find(receiver, name, MemberVisibility.InstanceAndStatic, new Scope(null), allowProperties: false, built.Model);
    }

    private sealed class Chain
    {
        public Chain()
        {
            Box.Modifiers = new MemberModifiers { IsReadonly = true };
            AddCopies(Kept, Box, Box);
            AddCopies(Again, Box, Kept);
            AddCopies(Other, Box, Box);
        }

        public HandBuiltModel Built { get; } = new();

        public ClassType Box { get; } = HandBuiltModel.Class("Box");

        public ClassType Kept { get; } = HandBuiltModel.Class("Kept");

        public ClassType Again { get; } = HandBuiltModel.Class("Again");

        public ClassType Other { get; } = HandBuiltModel.Class("Other");

        public ClassType this[string name] => name switch
        {
            "Box" => Box,
            "Kept" => Kept,
            "Again" => Again,
            _ => Other,
        };
    }
}