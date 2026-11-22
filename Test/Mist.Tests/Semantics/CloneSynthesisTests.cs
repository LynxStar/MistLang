using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CloneSynthesisTests
{
    private readonly HandBuiltModel model = new();
    private readonly StructType int32 = new("Int32", "Standard", new Scope(null), null, null);
    private readonly AbstractType clonable = new("IClonable<>", "Standard", new Scope(null), null, null);
    private readonly List<MistType> declared = [];

    public CloneSynthesisTests()
    {
        _ = model.Model.RegisterType(clonable);
    }

    [Fact]
    public void ADerivingTypeGetsAnOwnedReadonlyCloneOfItsOwnTypeBesideItsDeclaredMethodsAndTheConformance()
    {
        var point = Class("Point");
        Hold(point, "X", int32);

        Synthesize();

        var clone = Assert.IsType<Method>(point.DerivedClone);
        Assert.Empty(point.MethodsList);
        Assert.True(clone is { Name: "Clone", IsDerivedClone: true, Body: null, Modifiers: { IsOwned: true, IsReadonly: true, Accessibility: Accessibility.Public } });
        Assert.Same(point, clone.ReturnType);
        var conformance = Assert.IsType<ConstructedType>(Assert.Single(point.BaseAbstractTypes));
        Assert.Same(point.DerivedCloneConformance, conformance);
        Assert.Same(clonable, conformance.Definition);
        Assert.Same(point, Assert.Single(conformance.TypeArguments));
    }

    [Fact]
    public void AGenericDefinitionReturnsItselfOverItsOwnParameters()
    {
        var box = Class("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");
        Hold(box, "Value", t);

        Synthesize();

        var returned = Assert.IsType<ConstructedType>(box.DerivedClone?.ReturnType);
        Assert.Same(box, returned.Definition);
        Assert.Same(t, Assert.Single(returned.TypeArguments));
    }

    [Fact]
    public void ATypeThatDeclaresTheConformanceKeepsItAndGetsOnlyTheClone()
    {
        var point = Class("Point");
        Hold(point, "X", int32);
        point.BaseAbstractTypes.Add(model.Interner.GetOrCreateConstructedType(clonable, [point]));

        Synthesize();

        Assert.NotNull(point.DerivedClone);
        _ = Assert.Single(point.BaseAbstractTypes);
        Assert.Null(point.DerivedCloneConformance);
    }

    [Fact]
    public void ARefusedTypeAndATypeDeclaringCloneGetNothing()
    {
        var style = Class("Style");
        Hold(style, "Size", int32);
        var person = Class("Person");
        Hold(person, "Home", style);
        var config = Class("Config");
        config.MethodsList.Add(new Method("Clone", config, [], DeclarationFragments.Public) { DeclaringType = config });

        Synthesize();

        Assert.Null(person.DerivedClone);
        Assert.Empty(person.MethodsList);
        Assert.Null(config.DerivedClone);
        _ = Assert.Single(config.MethodsList);
    }

    [Theory]
    [InlineData("derived", true)]
    [InlineData("refused-instantiation", false)]
    [InlineData("linear", false)]
    [InlineData("unchangeable", false)]
    public void ASharedSourceIsClonedAtAnOwnedDestinationOnlyWhereItsOwnInstantiationDerivesAndCanChange(string shape, bool cloned)
    {
        var style = Class("Style");
        Hold(style, "Size", int32);
        var box = Class("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));
        var socket = Class("Socket");
        socket.Modifiers = DeclarationFragments.Public with { Discipline = SubstructuralDiscipline.Linear };
        Hold(socket, "Handle", int32);
        var margin = new StructType("Margin", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>()) { Modifiers = DeclarationFragments.Public };
        declared.Add(margin);
        Hold(margin, "Inset", int32);

        Synthesize();

        IMistType type = shape switch
        {
            "derived" => style,
            "refused-instantiation" => model.Interner.GetOrCreateConstructedType(box, [style]),
            "linear" => socket,
            _ => margin,
        };
        var source = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("shared", type));

        var bound = OwnedProvenance.At(model.Model.Clones, OwnershipKind.Owned, source);

        Assert.Equal(cloned, bound is CloneExpression);
        Assert.Same(source, bound is CloneExpression clone ? clone.Source : bound);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "MIST0178")]
    public void AnIClonableConstraintIsMetByAnInstantiationOnlyWhereItDerives(bool changeableArgument, string? expected)
    {
        var style = Class("Style");
        Hold(style, "Size", int32);
        var box = Class("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));
        var dup = Class("Dup");
        var u = DeclarationFragments.Parameter(dup, dup.Scope, "U");
        var required = new TypeConstraint(model.Interner.GetOrCreateConstructedType(clonable, [u]));
        u.ConstraintsList.Add(required);

        Synthesize();

        var argument = model.Interner.GetOrCreateConstructedType(box, [changeableArgument ? style : int32]);
        var closing = new TypeArgumentClosing(u, argument, HandBuiltModel.Node(), TypeSubstitution.Empty.With(u, argument));

        Assert.Equal(expected, ConstraintAnalyzer.Decide(closing, required, model.Interner, model.Model.Clones)?.Number.Value);
    }

    private void Synthesize()
    {
        CloneSynthesis.Run(declared, model.Model);
    }

    private ClassType Class(string name)
    {
        var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = DeclarationFragments.Public,
        };
        declared.Add(type);
        return type;
    }

    private static void Hold(ConcreteType holder, string name, IMistType type)
    {
        holder.FieldsList.Add(new Field(name, type, OwnershipKind.Unrestricted, default) { DeclaringType = holder });
    }
}