using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ScopeResolverNamedLookupFallbackTests
{
    [Fact]
    public void ObjectNamesTheSameTypeAsTheUnknownKeywordWithoutADeclaration()
    {
        var f = new BindingFragments();

        var spelled = f.Builder.Resolver.ResolveWithFailure(BindingFragments.TypeName("object"), f.Types);
        var keyword = f.Builder.Resolver.ResolveWithFailure(
            new TypeReferenceNode { First = new UnknownSingleTypeRefSingleTypeRefNode() }, f.Types);

        Assert.Same(UnknownType.Instance, keyword.Type);
        Assert.Same(keyword.Type, spelled.Type);
        Assert.False(spelled.IsFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ADeclaredTypeSpelledObjectWithACapitalStaysThatType()
    {
        var f = new BindingFragments();
        var declared = HandBuiltModel.Class("Object");
        f.Register(declared);

        var resolution = f.Builder.Resolver.ResolveWithFailure(BindingFragments.TypeName("Object"), f.Types);

        Assert.Same(declared, resolution.Type);
        Assert.False(resolution.IsFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ANestedTypeNameInheritedThroughTwoBasesIsAnAmbiguousTypeReference()
    {
        var f = new BindingFragments();
        var owner = new AbstractType("Combined", "Probe", new Scope(null), null, null);
        foreach(var name in new[] { "First", "Second" })
        {
            var parent = new AbstractType(name, "Probe", new Scope(null), null, null);
            parent.NestedTypes.Add(new ClassType("Part", "Probe", new Scope(null), parent, null) { Modifiers = DeclarationFragments.Public });
            owner.BaseAbstractTypes.Add(parent);
        }

        var node = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>(12);

        var resolution = f.Builder.Resolver.ResolveNamedTypeWithFailure("Part", owner.Scope, node);

        Assert.Same(ErrorType.Instance, resolution.Type);
        Assert.True(resolution.IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0299", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(13, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void BooleanTypeArgumentsAreLiftedAsTheirOwnBooleanValues()
    {
        var f = new BindingFragments();
        var arguments = new TypeArgumentListNode
        {
            TypeArgument = new FalseLiteralTypeArgumentTypeArgumentNode(),
            TypeArguments = [new TrueLiteralTypeArgumentTypeArgumentNode()],
        };

        var lifted = f.Builder.Resolver.ResolveTypeArguments(arguments, f.Types);

        Assert.Equal(2, lifted.Count);
        var no = Assert.IsType<LiftedType>(lifted[0]);
        var yes = Assert.IsType<LiftedType>(lifted[1]);
        Assert.Same(f.Built.Type("Boolean"), no.Type);
        Assert.Equal(false, no.Value);
        Assert.Same(f.Built.Type("Boolean"), yes.Type);
        Assert.Equal(true, yes.Value);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnUnresolvedOuterDefinitionFailsANestedReferenceWithOnlyItsOwnReport()
    {
        var f = new BindingFragments();
        var reference = BindingFragments.At<NestedGenericSingleTypeRefSingleTypeRefNode>(20);
        reference.Outer = BindingFragments.Name("Missing");
        reference.OuterArgs = BindingFragments.TypeArguments("Int32");
        reference.Nested = BindingFragments.Name("Inner");

        var resolution = f.Builder.Resolver.ResolveWithFailure(new TypeReferenceNode { First = reference }, f.Types);

        Assert.Same(ErrorType.Instance, resolution.Type);
        Assert.True(resolution.IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0298", diagnostic.Number.Value);
        Assert.Equal(21, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASpreadOverANameThatIsNotAPackIsRefusedAtTheSpread(bool typeParameter)
    {
        var f = new BindingFragments();
        f.Register(typeParameter
            ? new TypeParameter("Operand", 0, f.Types, null) { PackKind = TypePackKind.None }
            : HandBuiltModel.Class("Operand"));
        var spread = BindingFragments.At<SpreadSingleTypeRefSingleTypeRefNode>(16);
        spread.Name = BindingFragments.Name("Operand");

        var resolution = f.Builder.Resolver.ResolveWithFailure(new TypeReferenceNode { First = spread }, f.Types);

        Assert.Same(ErrorType.Instance, resolution.Type);
        Assert.True(resolution.IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(17, diagnostic.Position().Start.Column);
    }
}