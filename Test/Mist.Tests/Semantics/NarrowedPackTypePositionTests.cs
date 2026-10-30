using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: a written type resolved through hand-built body scopes, inside and outside the branch of a
// `TArgs is Type[2] narrowed` guard, and the guard's binding bound through PatternBinder.
[Trait("Tier", "2")]
public sealed class NarrowedPackTypePositionTests
{
    private readonly BindingFragments f = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    public NarrowedPackTypePositionTests()
    {
        f.Register(f.Built.Type("Array<,>"));
        f.Register(pack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AnIndexInsideTheProvenWidth_IsThePacksOwnElement(int index)
    {
        var branch = Guarded();

        var resolved = Resolve(Indexed("narrowed", index), branch);

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(pack.ElementAt(index), resolved);
    }

    [Fact]
    public void AnIndexPastTheProvenWidth_IsOutOfBounds()
    {
        var resolved = Resolve(Indexed("narrowed", 2), Guarded());

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0410", diagnostic.Number.Value);
        Assert.Equal("'narrowed[2]' is outside the pack: 'narrowed' is declared to hold exactly 2 element(s), indexed from 0.", diagnostic.Message);
        Assert.Same(ErrorType.Instance, resolved);
    }

    [Fact]
    public void TheNarrowedName_IsUnknownOutsideItsBranch()
    {
        _ = Guarded();

        _ = Resolve(Indexed("narrowed", 0), f.Scope);

        Assert.Equal("MIST0298", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void ATypeArrayNoGuardNarrowed_IsNoPack()
    {
        var branch = new BodyScope(f.Scope);
        branch.DeclareLocal(new LocalVariable("types", TypeArray(2)), HandBuiltModel.Node(), f.Diagnostics);

        _ = Resolve(Indexed("types", 0), branch);

        Assert.Equal("MIST0298", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void TheUnguardedPack_StaysUnproven()
    {
        _ = Resolve(Indexed("TArgs", 0), Guarded());

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0409", diagnostic.Number.Value);
        Assert.Contains("'if (TArgs is Type[N] fixed)'", diagnostic.Message);
    }

    [Theory]
    [InlineData("pack", true)]
    [InlineData("array", false)]
    public void OnlyAGuardOverAPack_RecordsThePackItNarrowed(string scrutinee, bool records)
    {
        var typed = BindingFragments.At<TypePatternPrimaryPatternNode>();
        typed.Type = new TypeReferenceNode { First = Indexed("Type", 2) };
        typed.Name = BindingFragments.Token("narrowed");
        var pattern = new PatternNode
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = typed } } },
        };

        var bound = Assert.IsType<TypeTestPattern>(PatternBinder.Bind(pattern, scrutinee == "pack" ? pack : TypeArray(null), f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(records ? pack : null, bound.Binding!.NarrowedPack);
    }

    private BodyScope Guarded()
    {
        var branch = new BodyScope(f.Scope);
        branch.DeclareLocal(new LocalVariable("narrowed", TypeArray(2)) { NarrowedPack = pack }, HandBuiltModel.Node(), f.Diagnostics);
        return branch;
    }

    private IMistType TypeArray(int? length)
    {
        return f.Builder.Resolver.ConstructArray(f.Built.Type("Type"), length);
    }

    private IMistType Resolve(NamedSingleTypeRefSingleTypeRefNode written, Scope scope)
    {
        return f.Builder.Resolver.Resolve(new TypeReferenceNode { First = written }, scope);
    }

    private static NamedSingleTypeRefSingleTypeRefNode Indexed(string name, int index)
    {
        var written = BindingFragments.At<NamedSingleTypeRefSingleTypeRefNode>();
        written.Name = BindingFragments.Name(name);
        written.ArrayRank = new SizedArrayRankArrayRankSuffixNode { Size = BindingFragments.Token(index.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
        return written;
    }
}