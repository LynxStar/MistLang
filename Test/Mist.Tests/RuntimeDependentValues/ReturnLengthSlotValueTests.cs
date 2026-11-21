using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "1")]
public sealed class ReturnLengthSlotValueTests
{
    private readonly BindingFragments f = new();
    private readonly TypeParameter length;

    public ReturnLengthSlotValueTests()
    {
        length = new TypeParameter("Length", 1, new Scope(null), null) { IsDependent = true, Domain = f.Built.Type("Int32") };
    }

    private Parameter Declared(bool isReadonly, bool isRef = false)
    {
        return new Parameter("n", f.Built.Type("Int32"), new ParameterModifier(isReadonly, isRef, OwnershipKind.Unrestricted));
    }

    private IReadOnlyList<string> Numbers()
    {
        return [.. f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value)];
    }

    [Fact]
    public void AReadonlyValueParameterNamedInTheReturnTypesLengthIsTheSlotsValue()
    {
        var parameter = Declared(isReadonly: true);
        var name = BindingFragments.Name("n");

        var held = Assert.IsType<ParameterValue>(f.Builder.Resolver.SlotValue(name, name, length, f.Scope, [parameter]));

        Assert.Same(parameter, held.Parameter);
        Assert.Same(name, held.Written);
        Assert.Equal("Standard.Int32", held.Domain.FullyQualifiedName);
        Assert.Empty(Numbers());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AParameterThatIsNotAReadonlyValueParameterIsRefusedAndBuildsNoLength(bool isReadonly, bool isRef)
    {
        var name = BindingFragments.Name("n");

        _ = Assert.IsType<ErrorType>(f.Builder.Resolver.SlotValue(name, name, length, f.Scope, [Declared(isReadonly, isRef)]));

        var refused = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0504", refused.Number.Value);
        Assert.Equal("'n' is named in the return type, so it must be declared as a readonly value parameter.", refused.Message);
    }

    [Fact]
    public void ASlotThatIsNotTheReturnTypesOwnLengthKeepsRefusingTheParametersName()
    {
        var name = BindingFragments.Name("n");

        _ = Assert.IsType<ErrorType>(f.Builder.Resolver.SlotValue(name, name, length, f.Scope, returnParameters: null));

        Assert.Equal(["MIST0487"], Numbers());
    }

    [Fact]
    public void ADottedPathThroughAParameterIsNotTheParameter()
    {
        var name = BindingFragments.Name("n");
        name.IDENTIFIERs = [BindingFragments.Token("Length")];

        _ = Assert.IsType<ErrorType>(f.Builder.Resolver.SlotValue(name, name, length, f.Scope, [Declared(isReadonly: true)]));

        Assert.Equal(["MIST0487"], Numbers());
    }
}