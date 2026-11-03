using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ExceptionErrorAdaptationBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandardConversionAdaptsTheForeignSourceWithoutClaimingSubtyping(bool derived)
    {
        var built = new HandBuiltModel();
        var exception = new ClassType("Exception", "System", null, null, null);
        var error = new AbstractType("Error", "Standard", null, null, null);
        var adapter = new ClassType("ExceptionError", "Standard", null, null, null)
        { Assembly = new MistAssembly("Standard", MistAssemblyKind.Standard) };
        adapter.BaseAbstractTypes.Add(error);
        var conversion = Conversion(adapter, exception, adapter);
        StandardErrorAdaptation.Register(adapter);
        var source = derived ? new ClassType("Cancellation", "Probe", null, null, null) { BaseType = exception } : exception;

        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(source, error, built.Interner));
        Assert.False(ConversionClassifier.IsFree(source, error, built.Interner));
        Assert.False(ConversionRelation.IsWithin(source, error));
        var converted = Assert.IsType<ImplicitConversionExpression>(ImplicitConversions.At(Literal(source), error, built.Interner));
        Assert.Same(conversion, converted.Operator);
        Assert.Same(adapter, converted.Type);
        Assert.Same(error, converted.TargetType);
    }

    [Fact]
    public void UserDeclarationsCannotInstallStandardAdaptation()
    {
        var exception = new ClassType("Exception", "System", null, null, null);
        var adapter = new ClassType("ExceptionError", "Standard", null, null, null);
        _ = Conversion(adapter, exception, adapter);
        StandardErrorAdaptation.Register(adapter);
        Assert.Null(exception.ErrorAdaptation);
    }
}