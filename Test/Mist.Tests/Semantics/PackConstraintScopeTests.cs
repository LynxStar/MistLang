using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class PackConstraintScopeTests
{
    [Fact]
    public void FindsThePacksConstraintThroughOrdinaryScopes()
    {
        var declaration = new Scope(null);
        var pack = DeclarePack("Ts", declaration);
        var constraint = new PackConstraintScope(declaration, pack);
        var lookup = new Scope(new Scope(constraint));

        Assert.True(PackConstraintScope.Constrains(lookup, pack));
    }

    [Fact]
    public void FindsTheOuterPacksConstraintPastADifferentPacksConstraint()
    {
        var outerDeclaration = new Scope(null);
        var outerPack = DeclarePack("Ts", outerDeclaration);
        var outerConstraint = new PackConstraintScope(outerDeclaration, outerPack);
        var innerDeclaration = new Scope(outerConstraint);
        var innerPack = DeclarePack("Us", innerDeclaration);
        var innerConstraint = new PackConstraintScope(innerDeclaration, innerPack);

        Assert.True(PackConstraintScope.Constrains(innerConstraint, outerPack));
    }

    [Fact]
    public void RejectsADistinctPackDeclarationWithTheSameName()
    {
        var outerDeclaration = new Scope(null);
        var outerPack = DeclarePack("Ts", outerDeclaration);
        var outerConstraint = new PackConstraintScope(outerDeclaration, outerPack);
        var innerDeclaration = new Scope(outerConstraint);
        var innerPack = DeclarePack("Ts", innerDeclaration);

        Assert.False(PackConstraintScope.Constrains(innerDeclaration, innerPack));
    }

    private static TypeParameter DeclarePack(string name, Scope scope)
    {
        return new TypeParameter(name, 0, scope, declaringType: null) { PackKind = TypePackKind.Unbracketed, IsDependent = true };
    }
}