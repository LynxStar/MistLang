using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1: one open constraint decision, the forwarded parameter and the requirement built directly.
[Trait("Tier", "1")]
public sealed class ConstraintEntailmentTests
{
    private const string Refused = "MIST0178";
    private const string RefusedConstructor = "MIST0179";

    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(TypeParameterConstraintKind.Class, TypeParameterConstraintKind.Class, null)]
    [InlineData(TypeParameterConstraintKind.Class, TypeParameterConstraintKind.Struct, Refused)]
    [InlineData(TypeParameterConstraintKind.Class, null, Refused)]
    [InlineData(TypeParameterConstraintKind.Struct, TypeParameterConstraintKind.Struct, null)]
    [InlineData(TypeParameterConstraintKind.Struct, TypeParameterConstraintKind.Stampable, null)]
    [InlineData(TypeParameterConstraintKind.Struct, TypeParameterConstraintKind.Default, Refused)]
    [InlineData(TypeParameterConstraintKind.Stampable, TypeParameterConstraintKind.Stampable, null)]
    [InlineData(TypeParameterConstraintKind.Stampable, TypeParameterConstraintKind.Struct, Refused)]
    [InlineData(TypeParameterConstraintKind.Default, TypeParameterConstraintKind.Default, null)]
    [InlineData(TypeParameterConstraintKind.Default, TypeParameterConstraintKind.Stampable, Refused)]
    [InlineData(TypeParameterConstraintKind.Default, null, Refused)]
    [InlineData(TypeParameterConstraintKind.Affine, TypeParameterConstraintKind.Linear, null)]
    [InlineData(TypeParameterConstraintKind.Relevant, TypeParameterConstraintKind.Linear, null)]
    [InlineData(TypeParameterConstraintKind.Linear, TypeParameterConstraintKind.Affine, Refused)]
    [InlineData(TypeParameterConstraintKind.Affine, TypeParameterConstraintKind.Relevant, Refused)]
    [InlineData(TypeParameterConstraintKind.Readonly, TypeParameterConstraintKind.Readonly, null)]
    [InlineData(TypeParameterConstraintKind.Readonly, TypeParameterConstraintKind.Mutable, Refused)]
    [InlineData(TypeParameterConstraintKind.Mutable, TypeParameterConstraintKind.Mutable, null)]
    [InlineData(TypeParameterConstraintKind.Owned, TypeParameterConstraintKind.Owned, null)]
    [InlineData(TypeParameterConstraintKind.Owned, null, Refused)]
    public void KeywordIsEntailedByTheDeclaredKeywordUnderItsLattice(
        TypeParameterConstraintKind required, TypeParameterConstraintKind? offered, string? expected)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");

        if(offered is { } declared)
        {
            forwarded.ConstraintsList.Add(new KeywordConstraint(declared));
        }

        Assert.Equal(expected, Judge(Closing(parameter, forwarded), new KeywordConstraint(required)));
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Class)]
    [InlineData(TypeParameterConstraintKind.Stampable)]
    public void KeywordOnABoundingParameterIsNotInherited(TypeParameterConstraintKind required)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var bounding = Parameter("V", 1);
        bounding.ConstraintsList.Add(new KeywordConstraint(required));
        forwarded.ConstraintsList.Add(new TypeConstraint(bounding));

        Assert.Equal(Refused, Judge(Closing(parameter, forwarded), new KeywordConstraint(required)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceIsEntailedOnlyWhenDeclared(bool declared)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var shown = Abstract("IShow");

        if(declared)
        {
            forwarded.ConstraintsList.Add(new TypeConstraint(shown));
        }

        Assert.Equal(declared ? null : Refused, Judge(Closing(parameter, forwarded), new TypeConstraint(shown)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BaseClassBoundReachesTheRequirementThroughInheritance(bool derives)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var animal = HandBuiltModel.Class("Animal");
        forwarded.ConstraintsList.Add(new TypeConstraint(derives ? HandBuiltModel.Class("Dog", animal) : HandBuiltModel.Class("Cat")));

        Assert.Equal(derives ? null : Refused, Judge(Closing(parameter, forwarded), new TypeConstraint(animal)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConstructedBoundIsSubstitutedBeforeItsBasesAreCompared(bool overItself)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var sibling = Parameter("V", 1);
        var ranked = Abstract("IRank");
        _ = DeclarationFragments.Parameter(ranked, ranked.Scope, "Item");
        var grade = HandBuiltModel.Class("Grade");
        var item = DeclarationFragments.Parameter(grade, grade.Scope, "T");
        grade.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(ranked, [item]));
        forwarded.ConstraintsList.Add(new TypeConstraint(built.Interner.GetOrCreateConstructedType(grade, [overItself ? forwarded : sibling])));
        var required = new TypeConstraint(built.Interner.GetOrCreateConstructedType(ranked, [parameter]));

        Assert.Equal(overItself ? null : Refused, Judge(Closing(parameter, forwarded), required));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelfReferencingBoundIsReadThroughTheClosing(bool overItself)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var sibling = Parameter("V", 1);
        var ranked = Abstract("IRank");
        _ = DeclarationFragments.Parameter(ranked, ranked.Scope, "Item");
        forwarded.ConstraintsList.Add(new TypeConstraint(built.Interner.GetOrCreateConstructedType(ranked, [overItself ? forwarded : sibling])));
        var required = new TypeConstraint(built.Interner.GetOrCreateConstructedType(ranked, [parameter]));

        Assert.Equal(overItself ? null : Refused, Judge(Closing(parameter, forwarded), required));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SiblingInTheRequirementIsTheClosedParameterNotItsName(bool sameParameter)
    {
        var key = Parameter("K");
        var value = Parameter("V", 1);
        var forwardedKey = Parameter("A");
        var forwardedValue = Parameter("B", 1);
        var impostor = Parameter("B", 1);
        var keyed = Abstract("IKeyed");
        _ = DeclarationFragments.Parameter(keyed, keyed.Scope, "Value");
        forwardedKey.ConstraintsList.Add(new TypeConstraint(
            built.Interner.GetOrCreateConstructedType(keyed, [sameParameter ? forwardedValue : impostor])));
        var required = new TypeConstraint(built.Interner.GetOrCreateConstructedType(keyed, [value]));
        var closing = Closing(key, forwardedKey, TypeSubstitution.Empty.With(value, forwardedValue));

        Assert.Equal(sameParameter ? null : Refused, Judge(closing, required));
    }

    [Fact]
    public void BoundOnAnotherParameterIsFollowed()
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var bounding = Parameter("V", 1);
        var shown = Abstract("IShow");
        bounding.ConstraintsList.Add(new TypeConstraint(shown));
        forwarded.ConstraintsList.Add(new TypeConstraint(bounding));

        Assert.Null(Judge(Closing(parameter, forwarded), new TypeConstraint(shown)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MutuallyBoundedParametersTerminate(bool declaredOnTheOther)
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var other = Parameter("V", 1);
        var shown = Abstract("IShow");
        forwarded.ConstraintsList.Add(new TypeConstraint(other));
        other.ConstraintsList.Add(new TypeConstraint(forwarded));

        if(declaredOnTheOther)
        {
            other.ConstraintsList.Add(new TypeConstraint(shown));
        }

        Assert.Equal(declaredOnTheOther ? null : Refused, Judge(Closing(parameter, forwarded), new TypeConstraint(shown)));
    }

    [Fact]
    public void EachRequirementIsJudgedOnItsOwn()
    {
        var parameter = Parameter("T");
        var forwarded = Parameter("U");
        var shown = Abstract("IShow");
        forwarded.ConstraintsList.Add(new TypeConstraint(shown));
        var closing = Closing(parameter, forwarded);

        Assert.Equal(Refused, Judge(closing, new KeywordConstraint(TypeParameterConstraintKind.Class)));
        Assert.Null(Judge(closing, new TypeConstraint(shown)));
    }

    [Fact]
    public void UnknownRequirementIsEntailedByAnything()
    {
        Assert.Null(Judge(Closing(Parameter("T"), Parameter("U")), new TypeConstraint(UnknownType.Instance)));
    }

    [Theory]
    [InlineData("new(), new(int)", null)]
    [InlineData("new(), new(int), new(bool)", null)]
    [InlineData("struct, new(int)", null)]
    [InlineData("new(int)", RefusedConstructor)]
    [InlineData("struct", RefusedConstructor)]
    [InlineData("new(string)", RefusedConstructor)]
    [InlineData("none", RefusedConstructor)]
    public void EveryRequiredConstructorMustBeDeclaredOrImpliedByStruct(string offered, string? expected)
    {
        var parameter = Parameter("T");
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        parameter.ConstraintsList.Add(new ConstructorConstraint([built.Type("Int32")]));
        var forwarded = Parameter("U");

        foreach(var guarantee in offered.Split(", "))
        {
            switch(guarantee)
            {
                case "struct":
                    forwarded.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
                    break;

                case "none":
                    break;

                default:
                    forwarded.ConstraintsList.Add(new ConstructorConstraint(
                        guarantee == "new()" ? [] : [built.Type(guarantee[4..^1] switch { "int" => "Int32", "bool" => "Boolean", _ => "String" })]));
                    break;
            }
        }

        var closing = Closing(parameter, forwarded);

        Assert.Equal(expected, Judge(closing, parameter.ConstraintsList[0]) ?? Judge(closing, parameter.ConstraintsList[1]));
    }

    [Fact]
    public void StructDoesNotEntailAParameterizedConstructor()
    {
        var parameter = Parameter("T");
        var required = new ConstructorConstraint([built.Type("Int32")]);
        parameter.ConstraintsList.Add(required);
        var forwarded = Parameter("U");
        forwarded.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));

        Assert.Equal(RefusedConstructor, Judge(Closing(parameter, forwarded), required));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConstructorSignatureIsComparedAfterTheClosingSubstitution(bool sameParameter)
    {
        var parameter = Parameter("T");
        var sibling = Parameter("V", 1);
        var required = new ConstructorConstraint([sibling]);
        parameter.ConstraintsList.Add(required);
        var forwarded = Parameter("U");
        var forwardedSibling = Parameter("B", 1);
        forwarded.ConstraintsList.Add(new ConstructorConstraint([sameParameter ? forwardedSibling : Parameter("B", 1)]));
        var closing = Closing(parameter, forwarded, TypeSubstitution.Empty.With(sibling, forwardedSibling));

        Assert.Equal(sameParameter ? null : RefusedConstructor, Judge(closing, required));
    }

    [Fact]
    public void FailedRequirementIsSilent()
    {
        Assert.Null(Judge(Closing(Parameter("T"), Parameter("U")), new TypeConstraint(ErrorType.Instance)));
    }

    [Fact]
    public void FailedSiblingInTheRequirementIsSilent()
    {
        var parameter = Parameter("T");
        var sibling = Parameter("V", 1);
        var keyed = Abstract("IKeyed");
        _ = DeclarationFragments.Parameter(keyed, keyed.Scope, "Value");
        var required = new TypeConstraint(built.Interner.GetOrCreateConstructedType(keyed, [sibling]));
        var closing = Closing(parameter, Parameter("U"), TypeSubstitution.Empty.With(sibling, ErrorType.Instance));

        Assert.Null(Judge(closing, required));
    }

    [Fact]
    public void FailedOfferedBoundIsSilent()
    {
        var forwarded = Parameter("U");
        forwarded.ConstraintsList.Add(new TypeConstraint(ErrorType.Instance));

        Assert.Null(Judge(Closing(Parameter("T"), forwarded), new TypeConstraint(Abstract("IShow"))));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, Refused)]
    public void APackElementOffersWhatItsPackDeclaresOfEachElement(bool rankedAgainstItself, string? expected)
    {
        var rank = Abstract("IRank<>");
        rank.TypeParametersList.Add(new TypeParameter("R", 0, rank.Scope, rank));
        var parameter = Parameter("T");
        var pack = new TypeParameter("Us", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };
        pack.ConstraintsList.Add(new TypeConstraint(built.Interner.GetOrCreateConstructedType(rank, [rankedAgainstItself ? pack : built.Type("Int32")])));

        Assert.Equal(expected, Judge(Closing(parameter, pack.ElementAt(1)), new TypeConstraint(built.Interner.GetOrCreateConstructedType(rank, [parameter]))));
    }

    [Fact]
    public void ClosedArgumentIsNotThisDecision()
    {
        var parameter = Parameter("T");
        var closing = new TypeArgumentClosing(parameter, built.Type("Int32"), HandBuiltModel.Node(), TypeSubstitution.Empty.With(parameter, built.Type("Int32")));

        Assert.Null(Judge(closing, new TypeConstraint(Abstract("IShow"))));
    }

    private string? Judge(TypeArgumentClosing closing, TypeParameterConstraint declared)
    {
        return ConstraintEntailment.Decide(closing, declared, built.Interner)?.Number.Value;
    }

    private static TypeParameter Parameter(string name, int ordinal = 0)
    {
        return new TypeParameter(name, ordinal, new Scope(null), null);
    }

    private static AbstractType Abstract(string name)
    {
        return new AbstractType(name, "Entailment", new Scope(null), null, null);
    }

    private static TypeArgumentClosing Closing(TypeParameter parameter, IMistType forwarded, TypeSubstitution? siblings = null)
    {
        return new TypeArgumentClosing(parameter, forwarded, HandBuiltModel.Node(), (siblings ?? TypeSubstitution.Empty).With(parameter, forwarded));
    }
}