using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class StampableConstraintEmissionTests
{
    [Fact]
    public void StampableConstraint_EmitsTheUnmanagedGuaranteeRequiredForGenericStackalloc()
    {
        Assert.Equal("where T : unmanaged", Emit(TypeParameterConstraintKind.Stampable));
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Struct, TypeParameterConstraintKind.Stampable)]
    [InlineData(TypeParameterConstraintKind.Stampable, TypeParameterConstraintKind.Struct)]
    [InlineData(TypeParameterConstraintKind.Stampable, TypeParameterConstraintKind.Stampable)]
    public void StampableConstraint_EmitsOneKindWithoutARedundantStructConstraint(
        TypeParameterConstraintKind first, TypeParameterConstraintKind second)
    {
        Assert.Equal("where T : unmanaged", Emit(first, second));
    }

    [Fact]
    public void StampableConstraint_DoesNotEmitTheImplicitParameterlessConstructorConstraint()
    {
        var parameter = Parameter(TypeParameterConstraintKind.Stampable);
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));

        Assert.Equal("where T : unmanaged", Emit(parameter));
    }

    [Fact]
    public void StampableConstraint_IsAValueKindForOptionalTypeEmission()
    {
        Assert.True(TypeNameEmitter.IsValueKind(Parameter(TypeParameterConstraintKind.Stampable)));
        Assert.False(TypeNameEmitter.IsValueKind(Parameter(TypeParameterConstraintKind.Readonly)));
    }

    private static string Emit(params TypeParameterConstraintKind[] kinds)
    {
        return Emit(Parameter(kinds));
    }

    private static string Emit(TypeParameter parameter)
    {
        var writer = new CSharpWriter();
        ConstraintEmitter.Emit([parameter], writer);
        return writer.ToString().Trim();
    }

    private static TypeParameter Parameter(params TypeParameterConstraintKind[] kinds)
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), declaringType: null);

        foreach(var kind in kinds)
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(kind));
        }

        return parameter;
    }
}