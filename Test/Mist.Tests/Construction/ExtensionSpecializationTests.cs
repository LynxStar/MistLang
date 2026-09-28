using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class ExtensionSpecializationTests
{
    [Fact]
    public void ClosedSignature_PreservesTheCanonicalDeclarationWithoutReattachingItsBody()
    {
        var fixture = new Fixture();
        var parameter = new Parameter("value", fixture.T);
        var local = new LocalVariable("value", fixture.T) { Parameter = parameter };
        var body = HandBuiltModel.Block(new ReturnStatement(
            new LocalReferenceExpression(HandBuiltModel.Node(), local), fixture.T));
        var declaration = new Method("Echo", fixture.T, [parameter], default)
        {
            Body = body,
            Scope = fixture.Block.Scope,
            DeclaringBlock = fixture.Block,
            DeclarationNode = HandBuiltModel.Node(17),
        };

        var closed = fixture.Close(declaration, fixture.Integer);

        Assert.Same(declaration, closed.SourceDeclaration);
        Assert.Same(body, closed.SourceDeclaration!.Body);
        Assert.Null(closed.Body);
        Assert.NotSame(parameter, closed.ParametersList[0]);
        Assert.Same(parameter, local.Parameter);
        Assert.Same(fixture.T, local.Type);
        Assert.Same(declaration.DeclarationNode, closed.DeclarationNode);
        Assert.Same(declaration.Scope, closed.Scope);
        Assert.Same(fixture.Block, closed.DeclaringBlock);
        Assert.Same(fixture.Block, closed.ClosedBlock);
        Assert.Same(fixture.Integer, closed.ReturnType);
        Assert.Same(fixture.Integer, closed.ParametersList[0].Type);
    }

    [Fact]
    public void ContextBindings_CloseExactTypesAndKeepIdentifiersAndMethodTypeParameters()
    {
        var fixture = new Fixture();
        var own = new TypeParameter("U", 0, new Scope(fixture.Block.Scope), null);
        var constraint = new TypeConstraint(fixture.T);
        own.ConstraintsList.Add(constraint);
        var context = HandBuiltModel.Class("Context");
        var declaration = new Method("Map", own, [new Parameter("input", fixture.T), new Parameter("other", own)], default);
        declaration.TypeParametersList.Add(own);
        declaration.ContextBindingsList.Add(new ContextBinding(new ConstructedType(context, [fixture.T, own]), "ambient"));
        declaration.ContextBindingsList.Add(new ContextBinding(fixture.T, "seed"));

        var integerView = fixture.Close(declaration, fixture.Integer);
        var textView = fixture.Close(declaration, fixture.Text);

        Assert.Collection(integerView.ContextBindingsList,
            binding =>
            {
                Assert.Equal("ambient", binding.Identifier);
                var type = Assert.IsType<ConstructedType>(binding.ContextType);
                Assert.Same(context, type.Definition);
                Assert.Equal(new IMistType[] { fixture.Integer, own }, type.TypeArguments);
            },
            binding =>
            {
                Assert.Equal("seed", binding.Identifier);
                Assert.Same(fixture.Integer, binding.ContextType);
            });
        Assert.Equal(new IMistType[] { fixture.Text, own }, Assert.IsType<ConstructedType>(textView.ContextBindingsList[0].ContextType).TypeArguments);
        Assert.Same(own, integerView.TypeParametersList[0]);
        Assert.Same(own, integerView.ReturnType);
        Assert.Same(own, integerView.ParametersList[1].Type);
        Assert.Same(fixture.Integer, integerView.BlockClosing.Apply(fixture.T, fixture.Interner));
        Assert.Same(fixture.Text, textView.BlockClosing.Apply(fixture.T, fixture.Interner));
        Assert.Same(own, textView.BlockClosing.Apply(own, fixture.Interner));
        Assert.Same(constraint, Assert.Single(integerView.TypeParametersList[0].ConstraintsList));
        Assert.Same(fixture.Integer, integerView.BlockClosing.Apply(constraint.ConstraintType, fixture.Interner));
        Assert.Same(fixture.Text, textView.BlockClosing.Apply(constraint.ConstraintType, fixture.Interner));
        Assert.Same(fixture.T, Assert.IsType<ConstructedType>(declaration.ContextBindingsList[0].ContextType).TypeArguments[0]);
        integerView.ContextBindingsList.Clear();
        Assert.Equal(2, declaration.ContextBindingsList.Count);
        Assert.Equal(2, textView.ContextBindingsList.Count);
    }

    [Theory]
    [InlineData(OwnershipKind.Unrestricted, ThisTier.None)]
    [InlineData(OwnershipKind.Owned, ThisTier.Owned)]
    [InlineData(OwnershipKind.Frozen, ThisTier.Frozen)]
    [InlineData(OwnershipKind.Borrowed, ThisTier.Borrowed)]
    [InlineData(OwnershipKind.Unrestricted, ThisTier.ReadonlyRef)]
    [InlineData(OwnershipKind.Unrestricted, null)]
    public void OwnershipAndReceiverContracts_AreCopiedExactly(OwnershipKind ownership, ThisTier? tier)
    {
        var fixture = new Fixture();
        var modifiers = new MemberModifiers { IsPure = true, IsLocal = true, IsRef = true };
        var declaration = new Method("Take", fixture.T, [], modifiers)
        {
            ReturnOwnership = ownership,
            DeclaredThisTier = tier,
            SequenceLengthOperation = SequenceLengthOperation.Preserved,
        };

        var closed = fixture.Close(declaration, fixture.Integer);

        Assert.Equal(ownership, closed.ReturnOwnership);
        Assert.Equal(tier, closed.DeclaredThisTier);
        Assert.Equal(modifiers, closed.Modifiers);
        Assert.Equal(declaration.SequenceLengthOperation, closed.SequenceLengthOperation);
    }

    [Fact]
    public void ReclosingAView_UsesTheCanonicalSignatureAndANewSubstitution()
    {
        var fixture = new Fixture();
        var declaration = new Method("Echo", fixture.T, [new Parameter("value", fixture.T)], default);
        var first = fixture.Close(declaration, fixture.Integer);
        var repeated = fixture.Close(first, fixture.Text);

        Assert.Same(declaration, repeated.SourceDeclaration);
        Assert.Same(fixture.Text, repeated.ReturnType);
        Assert.Same(fixture.Text, repeated.ParametersList[0].Type);
        Assert.Same(fixture.Text, repeated.BlockClosing.Apply(fixture.T, fixture.Interner));
        Assert.Same(fixture.Integer, first.ReturnType);
        Assert.Same(fixture.Integer, first.BlockClosing.Apply(fixture.T, fixture.Interner));
        Assert.Same(fixture.T, declaration.ReturnType);
        Assert.Null(declaration.SourceDeclaration);
    }

    [Fact]
    public void DeclarationAndParameterAttributes_ArePreservedInIndependentLists()
    {
        var fixture = new Fixture();
        var attribute = new AttributeData(HandBuiltModel.Class("Marker"), [], []);
        var written = new DeclaredAttribute(attribute.AttributeType, [], []);
        var parameter = new Parameter("values", fixture.T, new ParameterModifier(true, true, OwnershipKind.Frozen)) { IsFlattened = true };
        parameter.AttributesList.Add(attribute);
        parameter.DeclaredAttributesList.Add(written);
        var declaration = new Method("Inspect", fixture.T, [parameter], default);
        declaration.AttributesList.Add(attribute);
        declaration.DeclaredAttributesList.Add(written);

        var closed = fixture.Close(declaration, fixture.Integer);

        Assert.Same(attribute, Assert.Single(closed.AttributesList));
        Assert.Same(written, Assert.Single(closed.DeclaredAttributesList));
        Assert.Same(attribute, Assert.Single(closed.ParametersList[0].AttributesList));
        Assert.Same(written, Assert.Single(closed.ParametersList[0].DeclaredAttributesList));
        Assert.Equal(parameter.Modifier, closed.ParametersList[0].Modifier);
        Assert.True(closed.ParametersList[0].IsFlattened);
        closed.AttributesList.Clear();
        closed.ParametersList[0].DeclaredAttributesList.Clear();
        _ = Assert.Single(declaration.AttributesList);
        _ = Assert.Single(parameter.DeclaredAttributesList);
    }

    private sealed class Fixture
    {
        public HandBuiltModel Model { get; } = new();
        public TypeInterner Interner => Model.Interner;
        public ExtensionBlock Block { get; } = new("self", "Probe", new Scope(null), null);
        public TypeParameter T { get; }
        public ClassType Integer { get; } = HandBuiltModel.Class("Integer");
        public ClassType Text { get; } = HandBuiltModel.Class("Text");

        public Fixture()
        {
            T = new TypeParameter("T", 0, Block.Scope, null);
            Block.TypeParametersList.Add(T);
            Block.ResolvedReceiverType = T;
        }

        public Method Close(Method declaration, IMistType argument)
        {
            return ExtensionSpecialization.Close(declaration, Block, [argument], Interner);
        }
    }
}