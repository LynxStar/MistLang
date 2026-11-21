using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// Which length slot of a signature may name a parameter, over the resolver's chain from a hand-built type
/// reference: only the return type's own, never one nested in it, another type's or a parameter's type.
/// </summary>
[Trait("Tier", "2")]
public sealed class ReturnLengthResolutionTests
{
    private readonly BindingFragments f = new();
    private readonly Parameter n;

    public ReturnLengthResolutionTests()
    {
        var array = f.Built.Type("Array<,>");
        array.TypeParametersList.Add(new TypeParameter("T", 0, array.Scope, array));
        array.TypeParametersList.Add(new TypeParameter("Length", 1, array.Scope, array) { IsDependent = true, Domain = f.Built.Type("Int32") });
        f.Register(array);

        f.Register(HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", f.Built.Type("Int32")));
        var box = HandBuiltModel.Class("Box<>");
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        f.Register(box);

        n = new Parameter("n", f.Built.Type("Int32"), new ParameterModifier(IsReadonly: true, IsRef: false, OwnershipKind.Unrestricted));
    }

    private static TypeReferenceNode Sized(string name, bool optional = false)
    {
        var rank = BindingFragments.At<NamedSizedArrayRankArrayRankSuffixNode>();
        rank.Length = BindingFragments.Name(name);

        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
                ArrayRank = rank,
                Optional = optional ? BindingFragments.Token("?") : null,
            },
        };
    }

    private static TypeReferenceNode Generic(string definition, TypeReferenceNode argument)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = BindingFragments.Name(definition),
                TypeArgs = new TypeArgumentListNode { TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = argument } },
            },
        };
    }

    private IReadOnlyList<string> Numbers()
    {
        return [.. f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value)];
    }

    [Fact]
    public void TheReturnTypesOwnLengthHoldsTheParameter()
    {
        var returned = Assert.IsType<ConstructedType>(f.Builder.Resolver.ResolveReturnType(Sized("n"), f.Scope, [n]));

        Assert.Same(n, Assert.IsType<ParameterValue>(returned.TypeArguments[1]).Parameter);
        Assert.Equal("int[n]", TypeDisplay.Mist(returned));
        Assert.Empty(Numbers());
    }

    [Fact]
    public void ALengthNestedInTheReturnTypeKeepsRefusingTheName()
    {
        _ = f.Builder.Resolver.ResolveReturnType(Generic("Box", Sized("n")), f.Scope, [n]);

        Assert.Equal(["MIST0487"], Numbers());
    }

    [Fact]
    public void AnotherTypesSlotInTheReturnTypeKeepsRefusingTheName()
    {
        _ = f.Builder.Resolver.ResolveReturnType(Generic("Ring", BindingFragments.TypeName("n")), f.Scope, [n]);

        Assert.Equal(["MIST0487"], Numbers());
    }

    [Fact]
    public void AnOptionalArrayIsNotAnArrayReturnType()
    {
        _ = f.Builder.Resolver.ResolveReturnType(Sized("n", optional: true), f.Scope, [n]);

        Assert.Equal(["MIST0487"], Numbers());
    }

    [Fact]
    public void AParametersTypeKeepsRefusingTheName()
    {
        _ = f.Builder.Resolver.Resolve(Sized("n"), f.Scope);

        Assert.Equal(["MIST0487"], Numbers());
    }
}