using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1: one closed constraint decision with directly constructed types and substitutions.
[Trait("Tier", "1")]
public sealed class ConstraintRefusalTests
{
    [Theory]
    [InlineData("class-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("constructor-constraint", "MIST_PA_CONSTRUCTOR_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("constructor-constraint-naming-a-sibling-parameter", "MIST_PA_CONSTRUCTOR_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("extension-method-constraint-naming-the-blocks-parameter", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("f-bounded-constraint-closed-over-the-wrong-type", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("f-bounded-constraint-in-a-base-clause", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("generic-method-constraint-naming-the-receivers-parameter", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("generic-method-explicit-against-a-sibling-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("generic-method-group-against-a-self-referencing-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("generic-method-group-inferred-against-its-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("generic-method-inferred-against-a-self-referencing-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("generic-method-inferred-against-a-sibling-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("linear-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("pack-element-against-a-constraint-naming-its-pack", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("readonly-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("sibling-constraint-closed-by-a-supertype", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("sibling-constraint-inside-a-constructed-constraint", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("stampable-constraint-with-a-reference-argument", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    [InlineData("stampable-constraint-with-unproven-forwarding", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    public void RefusesNamedCase(string name, string expected)
    {
        var built = new HandBuiltModel();
        var (closing, constraint) = Build(built, name);
        var decision = ConstraintAnalyzer.Decide(closing, constraint, built.Interner);
        _ = Assert.NotNull(decision);
        Assert.Equal(expected, decision.Value.Id.Value);
    }

    [Fact]
    public void FailedSiblingConstraintIsSilent()
    {
        var built = new HandBuiltModel();
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var sibling = new TypeParameter("U", 1, new Scope(null), null);
        var closing = new TypeArgumentClosing(parameter, built.Type("Int32"), HandBuiltModel.Node(),
            TypeSubstitution.Empty.With(sibling, ErrorType.Instance));
        Assert.Null(ConstraintAnalyzer.Decide(closing, new TypeConstraint(sibling), built.Interner));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SatisfiedSiblingAndSelfConstraintsAreSilent(bool self)
    {
        var built = new HandBuiltModel();
        var (closing, constraint) = Build(built, self
            ? "f-bounded-constraint-closed-over-the-wrong-type"
            : "sibling-constraint-closed-by-a-supertype");

        if(self)
        {
            var argument = Assert.IsType<ClassType>(closing.Argument);
            var implemented = Assert.IsType<ConstructedType>(Assert.Single(argument.BaseAbstractTypes));
            argument.BaseAbstractTypes.Clear();
            argument.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(implemented.Definition, [argument]));
        }
        else
        {
            var required = Assert.IsType<TypeConstraint>(constraint);
            var dog = HandBuiltModel.Class("Puppy", Assert.IsType<ClassType>(closing.Closing.Apply(required.ConstraintType, built.Interner)));
            closing = closing with { Argument = dog };
        }

        Assert.Null(ConstraintAnalyzer.Decide(closing, constraint, built.Interner));
    }

    [Theory]
    [InlineData("Score", null)]
    [InlineData("Grade", "MIST_PA_CONSTRAINT_NOT_SATISFIED")]
    public void AWholePackConstraintSpreadingItsPack_IsJudgedPerElementAgainstTheWholeClosing(string element, string? expected)
    {
        var built = new HandBuiltModel();
        var pack = new TypeParameter("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
        var all = new AbstractType("IAll", "Refusal", new Scope(null), null, null);
        _ = DeclarationFragments.Parameter(all, all.Scope, "Item");
        var score = HandBuiltModel.Class("Score");
        var grade = HandBuiltModel.Class("Grade");
        score.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(all, [built.Interner.GetOrCreateTupleType([score, grade])]));
        grade.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(all, [built.Interner.GetOrCreateTupleType([score, score])]));
        var argument = element == "Score" ? score : grade;
        var closed = TypeSubstitution.ForParameters([pack], [built.Interner.LiftPack(built.Type("Type"), new TypePackValue([score, grade]))]);
        var closing = new TypeArgumentClosing(pack, argument, HandBuiltModel.Node(25), closed.With(pack, argument));
        var constraint = new TypeConstraint(built.Interner.GetOrCreateConstructedType(all, [new TupleType([new SpreadType(pack)])]));

        Assert.Equal(expected, ConstraintAnalyzer.Decide(closing, constraint, built.Interner)?.Id.Value);
    }

    private static (TypeArgumentClosing Closing, TypeParameterConstraint Constraint) Build(HandBuiltModel built, string name)
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null)
        {
            PackKind = name == "pack-element-against-a-constraint-naming-its-pack" ? TypePackKind.Unbracketed : TypePackKind.None
        };
        IMistType argument;
        TypeParameterConstraint constraint;
        var substitution = TypeSubstitution.Empty;

        switch(name)
        {
            case "class-constraint":
                argument = HandBuiltModel.Struct("SomeStruct");
                constraint = new KeywordConstraint(TypeParameterConstraintKind.Class);
                break;

            case "linear-constraint":
                argument = built.Type("Int32");
                constraint = new KeywordConstraint(TypeParameterConstraintKind.Linear);
                break;

            case "readonly-constraint":
                argument = HandBuiltModel.Class("Mutable");
                constraint = new KeywordConstraint(TypeParameterConstraintKind.Readonly);
                break;

            case "stampable-constraint-with-a-reference-argument":
            case "stampable-constraint-with-unproven-forwarding":
                argument = name == "stampable-constraint-with-a-reference-argument"
                    ? built.Type("String")
                    : new TypeParameter("U", 1, new Scope(null), null);

                if(argument is TypeParameter forwarding)
                {
                    forwarding.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
                }

                constraint = new KeywordConstraint(TypeParameterConstraintKind.Stampable);
                break;

            case "constructor-constraint":
            case "constructor-constraint-naming-a-sibling-parameter":
                var constructed = HandBuiltModel.Class("Widget");
                constructed.ConstructorsList.Add(new Constructor("Widget", constructed,
                    name == "constructor-constraint" ? [] : [new Parameter("size", built.Type("Int32"))], DeclarationFragments.Public));
                argument = constructed;

                if(name == "constructor-constraint")
                {
                    constraint = new ConstructorConstraint([built.Type("Int32")]);
                }
                else
                {
                    var sibling = new TypeParameter("A", 1, new Scope(null), null);
                    substitution = substitution.With(sibling, built.Type("String"));
                    constraint = new ConstructorConstraint([sibling]);
                }

                break;

            case "generic-method-group-inferred-against-its-constraint":
                argument = built.Type("String");
                constraint = new TypeConstraint(new AbstractType("IShow", "Refusal", new Scope(null), null, null));
                break;

            case "generic-method-explicit-against-a-sibling-constraint":
            case "generic-method-inferred-against-a-sibling-constraint":
            case "sibling-constraint-inside-a-constructed-constraint":
                var keyed = new AbstractType("IKeyed", "Refusal", new Scope(null), null, null);
                var key = DeclarationFragments.Parameter(keyed, keyed.Scope, "K");
                var supplied = new TypeParameter("K", 1, new Scope(null), null);
                var badge = HandBuiltModel.Class("Badge");
                badge.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(keyed, [built.Type("String")]));
                argument = badge;
                substitution = substitution.With(supplied, built.Type("Int32"));
                constraint = new TypeConstraint(built.Interner.GetOrCreateConstructedType(keyed, [supplied]));
                _ = key;
                break;

            case "extension-method-constraint-naming-the-blocks-parameter":
            case "generic-method-constraint-naming-the-receivers-parameter":
            case "sibling-constraint-closed-by-a-supertype":
                var animal = HandBuiltModel.Class("Animal");
                var dog = HandBuiltModel.Class("Dog", animal);
                var receiver = new TypeParameter("U", 1, new Scope(null), null);
                argument = animal;
                substitution = substitution.With(receiver, dog);
                constraint = new TypeConstraint(receiver);
                break;

            default:
                var ranked = new AbstractType("IRank", "Refusal", new Scope(null), null, null);
                _ = DeclarationFragments.Parameter(ranked, ranked.Scope, "Item");
                var score = HandBuiltModel.Class("Score");
                var grade = HandBuiltModel.Class("Grade");
                grade.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(ranked, [score]));
                argument = grade;
                constraint = new TypeConstraint(built.Interner.GetOrCreateConstructedType(ranked, [parameter]));
                break;
        }

        substitution = substitution.With(parameter, argument);
        return (new TypeArgumentClosing(parameter, argument, HandBuiltModel.Node(25), substitution), constraint);
    }
}