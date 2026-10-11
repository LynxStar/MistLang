using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using SourceSpan = Storm.Runtime.SourceSpan;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class LocalBindingContractRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateLocalReportsTheSecondNameTokenInItsOwnSource(bool conflictsWithFunction)
    {
        var scope = new BodyScope(new Scope(null));
        var diagnostics = new DiagnosticBag();
        var original = new LocalVariable("item", ErrorType.Instance);
        if(conflictsWithFunction)
        {
            var function = new Method("item", ErrorType.Instance, [], new MemberModifiers());
            scope.DeclareLocalFunction(new LocalFunctionDeclarationNode(), function, diagnostics);
        }
        else
        {
            scope.DeclareLocal(original, HandBuiltModel.Node(4, "original.mist"), diagnostics);
        }

        var source = new SourceText("later.mist", "Int32\n  item = 2;");
        var declaration = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Origin = new ParsedOrigin(source),
            Span = new SourceSpan(0, source.Contents.Length),
        };
        scope.DeclareLocal(new LocalVariable("item", ErrorType.Instance), declaration, diagnostics, new SourceSpan(8, 4));

        var duplicate = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_DUPLICATE_LOCAL", duplicate.Id.Value);
        Assert.Equal("later.mist", duplicate.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(2, 3), new LinePosition(2, 7)), duplicate.Location);
        if(!conflictsWithFunction)
        {
            Assert.Same(original, scope.Locals["item"]);
        }
    }

    [Fact]
    public void ALocalInAChildScopeCanShadowItsParent()
    {
        var parent = new BodyScope(new Scope(null));
        var child = new BodyScope(parent);
        var diagnostics = new DiagnosticBag();
        var outer = new LocalVariable("item", ErrorType.Instance);
        var inner = new LocalVariable("item", ErrorType.Instance);
        var site = HandBuiltModel.Node();
        parent.DeclareLocal(outer, site, diagnostics);
        child.DeclareLocal(inner, site, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Same(outer, parent.Locals["item"]);
        Assert.Same(inner, child.Locals["item"]);
    }

    [Fact]
    public void DiscardIdentitiesNeverEnterTheSourceNameTable()
    {
        var scope = new BodyScope(new Scope(null));
        var diagnostics = new DiagnosticBag();
        var site = HandBuiltModel.Node();
        scope.DeclareLocal(new LocalVariable("_", ErrorType.Instance) { IsDiscard = true }, site, diagnostics);
        scope.DeclareLocal(new LocalVariable("_", ErrorType.Instance) { IsDiscard = true }, site, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Empty(scope.Locals);
        _ = Assert.IsType<NotFoundResult>(scope.Lookup(new LookupRequest("_", MemberVisibility.InstanceAndStatic, site)));
    }

    [Fact]
    public void DuplicateLocalFunctionsRetainTheirOwningDeclarationAndDescriptor()
    {
        var scope = new BodyScope(new Scope(null));
        var diagnostics = new DiagnosticBag();
        var earlier = new LocalFunctionDeclarationNode();
        var later = BindingFragments.At<LocalFunctionDeclarationNode>(19);
        var first = new Method("Same", ErrorType.Instance, [], new MemberModifiers());
        var second = new Method("Same", ErrorType.Instance, [], new MemberModifiers());
        scope.DeclareLocalFunction(earlier, first, diagnostics);
        scope.DeclareLocalFunction(later, second, diagnostics);

        var duplicate = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_DUPLICATE_LOCAL_FUNCTION", duplicate.Id.Value);
        Assert.Equal("hand-built.mist", duplicate.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 20), new LinePosition(1, 21)), duplicate.Location);
        Assert.Same(second, scope.LocalFunctions[later]);
    }
}