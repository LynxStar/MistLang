using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class CloneRefusalSiteTests
{
    private readonly HandBuiltModel model = new();
    private readonly StructType int32 = new("Int32", "Standard", new Scope(null), null, null);
    private readonly List<MistType> declared = [];

    [Fact]
    public void AnOwnedDestinationFedATypeWithNoCloneNamesTheSubclassAndFieldThatPreventDerivation()
    {
        var animal = Class("Animal");
        Hold(animal, "Legs", int32);
        var cat = Class("Cat", animal);
        Hold(cat, "Friend", animal);

        var diagnostic = Assert.Single(Run(animal, value => new LocalDeclarationStatement(new LocalVariable("mine", animal) { Ownership = OwnershipKind.Owned }, value)));

        Assert.Equal("MIST0264", diagnostic.Number.Value);
        Assert.Equal(1, diagnostic.Position().Start.Line);
        Assert.Equal(25, diagnostic.Position().Start.Column);
        Assert.Equal(
            "Cannot move this value into owned binding 'mine': parameter 'a' is not owned, so the caller may still hold its value; 'Animal' cannot be cloned: its subclass 'Cat' cannot be copied, because field 'Friend' of 'Cat' is not owned and holds 'Animal', which can change; make that field 'owned' or give the type a 'Clone()' of its own. An owned destination takes only what nothing else holds: a construction, a clone, a call returning owned, a move from an owned binding, or a value of a type that cannot change.",
            diagnostic.Message);
    }

    [Fact]
    public void AnOwnedDestinationFedATypeWhoseBaseDeclaresCloneNamesThatClone()
    {
        var animal = Class("Animal");
        animal.MethodsList.Add(new Method("Clone", animal, [], DeclarationFragments.Public with { IsOwned = true }) { DeclaringType = animal });
        var dog = Class("Dog", animal);
        Hold(dog, "Name", int32);

        var diagnostic = Assert.Single(Run(dog, value => new LocalDeclarationStatement(new LocalVariable("mine", dog) { Ownership = OwnershipKind.Owned }, value)));

        Assert.Contains(
            "; 'Dog' cannot be cloned: its base class 'Animal' declares 'Clone()', which copies every value as class 'Animal', and which 'Dog' cannot declare again. An owned destination",
            diagnostic.Message);
    }

    [Fact]
    public void AnExplicitCloneOfAnInstantiationThatDoesNotDeriveIsRefusedAtTheCall()
    {
        var style = Class("Style");
        Hold(style, "Size", int32);
        var box = Class("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));
        var boxOfStyle = model.Interner.GetOrCreateConstructedType(box, [style]);
        var clone = new Method("Clone", boxOfStyle, [], DeclarationFragments.Public with { IsOwned = true, IsReadonly = true }) { DeclaringType = box, IsDerivedClone = true };
        box.MethodsList.Add(clone);
        box.DerivedClone = clone;

        var diagnostics = Run(boxOfStyle, value => new ExpressionStatement(new InvocationExpression(
            HandBuiltModel.Node(40), new MemberAccessExpression(HandBuiltModel.Node(40), value, "Clone", boxOfStyle), [], boxOfStyle, clone, [])));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MIST0506", diagnostic.Number.Value);
        Assert.Equal(41, diagnostic.Position().Start.Column);
        Assert.Equal(
            "Cannot clone this 'Box<Style>': 'Box<Style>' cannot be cloned: field 'Value' of 'Box' is not owned and holds 'Style', which can change; make that field 'owned' or give the type a 'Clone()' of its own.",
            diagnostic.Message);
    }

    private IReadOnlyList<Diagnostic> Run(IMistType parameterType, Func<Expression, Statement> statement)
    {
        var parameter = new Parameter("a", parameterType);
        var source = new LocalReferenceExpression(HandBuiltModel.Node(24), new LocalVariable("a", parameterType) { Parameter = parameter });
        var body = HandBuiltModel.Block(statement(source), new ReturnStatement(null, null));
        var probe = Class("Probe");
        var run = new Method("Run", model.Type("Void"), [parameter], default) { DeclaringType = probe, Body = body };
        probe.MethodsList.Add(run);

        return model.Analyze(new OwnershipAnalyzer(), body, declared, probe, run);
    }

    private ClassType Class(string name, MistType? baseType = null)
    {
        var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = DeclarationFragments.Public,
            BaseType = baseType,
        };
        declared.Add(type);
        return type;
    }

    private static void Hold(ConcreteType holder, string name, IMistType type)
    {
        holder.FieldsList.Add(new Field(name, type, OwnershipKind.Unrestricted, default) { DeclaringType = holder });
    }
}