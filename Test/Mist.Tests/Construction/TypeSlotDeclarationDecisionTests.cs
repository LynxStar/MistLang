using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class TypeSlotDeclarationDecisionTests
{
    private readonly BindingFragments f = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void ASecondSlotOfTheSameNameIsRefusedWhateverEitherSlotsArity(int firstArity, int secondArity)
    {
        var shape = Shape();
        var builder = Builder();

        builder.Resolve(Slot("Converted", firstArity, offset: 10), shape);
        builder.Resolve(Slot("Converted", secondArity, offset: 40), shape);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0038", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(41, diagnostic.Position().Start.Column);
        Assert.Contains("'Shape'", diagnostic.Message);
        Assert.Contains("'Converted'", diagnostic.Message);
        Assert.Equal(firstArity, Assert.Single(shape.TypeSlotsList).TypeParametersList.Count);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("static")]
    [InlineData("public static")]
    public void ModifiersOnASlotAreRefusedOnceAndTheSlotIsStillDeclared(string written)
    {
        var shape = Shape();
        var slot = Slot("Item", 0, offset: 30);
        slot.Modifiers = [.. written.Split(' ').Select(Modifier)];

        Builder().Resolve(slot, shape);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0039", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(31, diagnostic.Position().Start.Column);
        Assert.Equal("Item", Assert.Single(shape.TypeSlotsList).Name);
    }

    [Fact]
    public void DistinctlyNamedSlotsAreAllDeclaredInOrder()
    {
        var shape = Shape();
        var builder = Builder();

        builder.Resolve(Slot("Item", 0, offset: 10), shape);
        builder.Resolve(Slot("Items", 0, offset: 20), shape);
        builder.Resolve(Slot("Converted", 1, offset: 30), shape);

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(["Item", "Items", "Converted<>"], shape.TypeSlotsList.Select(slot => slot.Name));
    }

    private TypeSlotBuilder Builder()
    {
        return new TypeSlotBuilder(f.Builder, new ConstraintResolver(f.Builder, f.Builder.Resolver));
    }

    private AbstractType Shape()
    {
        return new AbstractType("Shape", "Probe", f.Types, null, null);
    }

    private static AssociatedTypeSlotDeclarationNode Slot(string name, int arity, int offset)
    {
        var slot = BindingFragments.At<AssociatedTypeSlotDeclarationNode>(offset);
        slot.Name = BindingFragments.Token(name);
        if(arity > 0)
        {
            var parameters = Enumerable.Range(0, arity)
                .Select(i => new RegularTypeParameterTypeParameterNode { Name = BindingFragments.Token($"T{i}") })
                .ToList();
            slot.TypeParams = new TypeParameterListNode { TypeParameter = parameters[0], TypeParameters = [.. parameters.Skip(1)] };
        }

        return slot;
    }

    private static ModifierNode Modifier(string written)
    {
        return written switch
        {
            "public" => new PublicModifierModifierNode(),
            "private" => new PrivateModifierModifierNode(),
            "static" => new StaticModifierModifierNode(),
            _ => throw new ArgumentException("Unknown modifier", nameof(written)),
        };
    }
}