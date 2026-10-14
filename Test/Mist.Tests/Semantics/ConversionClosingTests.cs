using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ConversionClosingTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void AConversionOfAGenericOwner_CarriesTheOwnersArguments()
    {
        var (shown, held, conversion) = Shown();
        var closed = built.Interner.GetOrCreateConstructedType(shown, [built.Type("Int32")]);

        var function = Assert.Single(ConversionRelation.DeclaredOn(closed));

        Assert.Same(conversion, function.Operator);
        Assert.Same(built.Type("Int32"), function.Closing.Apply(held, built.Interner));
    }

    [Fact]
    public void AConversionInheritedFromAGenericBase_CarriesTheBaseClausesArguments()
    {
        var (shown, held, conversion) = Shown();
        var derived = HandBuiltModel.Class("Derived", built.Interner.GetOrCreateConstructedType(shown, [built.Type("Int32")]));

        var function = Assert.Single(CastOperatorSelection.Best(derived, built.Type("String"), built.Interner));

        Assert.Same(conversion, function.Operator);
        Assert.Same(built.Type("Int32"), function.Closing.Apply(held, built.Interner));
    }

    private (ClassType Shown, TypeParameter Held, CastOperator Conversion) Shown()
    {
        var shown = HandBuiltModel.Class("Shown");
        var held = new TypeParameter("T", 0, shown.Scope, shown);
        shown.TypeParametersList.Add(held);
        var conversion = HandBuiltModel.Conversion(
            shown, built.Interner.GetOrCreateConstructedType(shown, [held]), built.Type("String"), isImplicit: false);

        return (shown, held, conversion);
    }
}