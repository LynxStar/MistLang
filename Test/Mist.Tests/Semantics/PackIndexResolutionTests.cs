using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class PackIndexResolutionTests
{
    private const int WrittenAt = 20;

    private readonly BindingFragments f = new();

    public PackIndexResolutionTests()
    {
        f.Register(f.Built.Type("Array<,>"));
    }

    [Fact]
    public void AnIndexIntoAPackOfUnknownLength_NamesThePackAndTheIndexAndFailsTheResolution()
    {
        f.Register(new TypeParameter("Ts", 0, f.Types, null) { PackKind = TypePackKind.Unbracketed });

        var resolution = Resolve("Ts", "1");

        Assert.True(resolution.IsFailed);
        Assert.Same(ErrorType.Instance, resolution.Type);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0409", diagnostic.Number.Value);
        Assert.StartsWith("'Ts[1]' indexes a pack whose length is not known here, so it may hold no element 1.", diagnostic.Message);
        Assert.Equal(WrittenAt + 1, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AnIndexPastAFixedSizePack_NamesTheIndexAndTheDeclaredSizeAndFailsTheResolution()
    {
        f.Register(new TypeParameter("Ts", 0, f.Types, null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 });

        var resolution = Resolve("Ts", "3");

        Assert.True(resolution.IsFailed);
        Assert.Same(ErrorType.Instance, resolution.Type);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0410", diagnostic.Number.Value);
        Assert.Equal("'Ts[3]' is outside the pack: 'Ts' is declared to hold exactly 2 element(s), indexed from 0.", diagnostic.Message);
        Assert.Equal(WrittenAt + 1, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AnIndexInsideAFixedSizePack_IsItsElementAndDoesNotFailTheResolution()
    {
        var pack = new TypeParameter("Ts", 0, f.Types, null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };
        f.Register(pack);

        var resolution = Resolve("Ts", "1");

        Assert.False(resolution.IsFailed);
        Assert.Same(pack.ElementAt(1), resolution.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ASizedRankOnAnOrdinaryTypeParameter_IsASizedArrayOfIt()
    {
        var parameter = new TypeParameter("T", 0, f.Types, null);
        f.Register(parameter);

        var resolution = Resolve("T", "2");

        Assert.False(resolution.IsFailed);
        Assert.Same(f.Builder.Resolver.ConstructArray(parameter, 2), resolution.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private TypeResolution Resolve(string name, string size)
    {
        var written = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>(WrittenAt);
        written.Name = BindingFragments.Name(name);
        written.ArrayRank = new SizedArrayRankArrayRankSuffixNode { Size = BindingFragments.Token(size) };

        return f.Builder.Resolver.ResolveWithFailure(new TypeReferenceNode { First = written }, f.Scope);
    }
}