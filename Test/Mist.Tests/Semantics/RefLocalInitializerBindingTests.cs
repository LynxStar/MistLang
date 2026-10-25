using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class RefLocalInitializerBindingTests
{
    [Theory]
    [InlineData("ref")]
    [InlineData("readonly ref")]
    public void ARefLocalWithoutAnInitializerIsRefusedOnceAndItsUsesAreNotBlamed(string written)
    {
        var f = new BindingFragments();
        OwnershipSpecifierNode specifier = written == "ref" ? new RefSpecifierOwnershipSpecifierNode() : new ReadonlyRefSpecifierOwnershipSpecifierNode();

        var local = Bind(f, specifier);

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0374", diagnostic.Number.Value);
        Assert.Contains("'r'", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(local.IsRef);
        Assert.True(local.IsFailed);
    }

    [Fact]
    public void AnOrdinaryLocalWithoutAnInitializerIsDeclaredForLaterAssignment()
    {
        var f = new BindingFragments();

        var local = Bind(f, specifier: null);

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.False(local.IsFailed);
    }

    private static LocalVariable Bind(BindingFragments f, OwnershipSpecifierNode? specifier)
    {
        var declaration = BindingFragments.At<UninitializedVariableDeclarationNode>();
        declaration.OwnershipSpecifier = specifier;
        declaration.Type = BindingFragments.TypeName("Int32");
        declaration.Name = BindingFragments.Token("r");
        var statement = new UninitializedVariableStatementStatementNode { UninitializedVariable = declaration };

        return Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(statement, f.Binding, f.Builder)).Local;
    }
}