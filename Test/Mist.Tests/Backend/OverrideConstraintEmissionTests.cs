using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class OverrideConstraintEmissionTests
{
    [Theory]
    [InlineData(false, "where T : class, IShow, new()")]
    [InlineData(true, "where T : class")]
    public void AnOverridingMethod_RestatesOnlyAClassOrStructConstraint(bool overriding, string expected)
    {
        var parameter = Parameter();
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Class));
        parameter.ConstraintsList.Add(new TypeConstraint(new AbstractType("IShow", "Shapes", new Scope(null), null, null)));
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));

        Assert.Equal([expected], ConstraintEmitter.Lines([parameter], liftSlots: true, overriding));
    }

    [Fact]
    public void AnOverridingMethod_WithOnlyATypeConstraint_WritesNoClause()
    {
        var parameter = Parameter();
        parameter.ConstraintsList.Add(new TypeConstraint(new AbstractType("IShow", "Shapes", new Scope(null), null, null)));

        Assert.Empty(ConstraintEmitter.Lines([parameter], liftSlots: true, overriding: true));
    }

    [Fact]
    public void AnOverridingMethod_KeepsAStructConstraint()
    {
        var parameter = Parameter();
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));

        Assert.Equal(["where T : struct"], ConstraintEmitter.Lines([parameter], liftSlots: true, overriding: true));
    }

    private static TypeParameter Parameter()
    {
        return new TypeParameter("T", 0, new Scope(null), declaringType: null);
    }
}