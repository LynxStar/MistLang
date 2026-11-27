using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.Tests.Helpers;
using SourceSpan = Storm.Runtime.SourceSpan;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class LocalFunctionModifierAttributionTests
{
    [Fact]
    public void IntrinsicIsReportedAtTheDeclaration()
    {
        AssertForbidden<IntrinsicModifierModifierNode>("intrinsic");
    }

    [Fact]
    public void BareReadonlyIsReportedAtTheDeclaration()
    {
        AssertForbidden<ReadonlyModifierModifierNode>("readonly");
    }

    [Fact]
    public void OwnedIsReportedAtTheDeclaration()
    {
        AssertForbidden<OwnedModifierModifierNode>("owned");
    }

    [Fact]
    public void RefIsReportedAtTheDeclaration()
    {
        AssertForbidden<RefModifierModifierNode>("ref");
    }

    [Fact]
    public void AffineIsReportedAtTheDeclaration()
    {
        AssertForbidden<AffineModifierModifierNode>("affine");
    }

    [Fact]
    public void PureIsAdmittedWithImpliedReadonly()
    {
        AssertAdmittedReadonly(new PureModifierModifierNode());
    }

    [Fact]
    public void LocalReadonlyIsAdmitted()
    {
        AssertAdmittedReadonly(new LocalModifierModifierNode(), new ReadonlyModifierModifierNode());
    }

    private static void AssertForbidden<TModifier>(string keyword) where TModifier : ModifierNode, new()
    {
        var declarationText = $"{keyword} Void Probe() {{ }}";
        var source = new SourceText("local-function.mist", $"    {declarationText}\n");
        var origin = new ParsedOrigin(source);
        var modifier = new TModifier { Origin = origin, Span = new SourceSpan(4, keyword.Length) };
        var declaration = Function(modifier);
        declaration.Origin = origin;
        declaration.Span = new SourceSpan(4, declarationText.Length);

        var fragments = Declare(declaration);

        var diagnostic = Assert.Single(fragments.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0369", diagnostic.Number.Value);
        Assert.Equal(new EnumValue(keyword), Assert.Single(diagnostic.Arguments));
        Assert.Equal(new SourceRange(new SourceIdentity(source), 4, declarationText.Length), diagnostic.Location);
    }

    private static void AssertAdmittedReadonly(params ModifierNode[] modifiers)
    {
        var declaration = Function(modifiers);

        var fragments = Declare(declaration);

        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
        Assert.True(fragments.Scope.LocalFunctions[declaration].Modifiers.IsReadonly);
    }

    private static LocalFunctionDeclarationNode Function(params ModifierNode[] modifiers)
    {
        return new()
        {
            Name = BindingFragments.Token("Probe"),
            ReturnType = BindingFragments.TypeName("Void"),
            Body = new BlockNode { Statements = [] },
            Modifiers = [.. modifiers],
        };
    }

    private static BindingFragments Declare(LocalFunctionDeclarationNode declaration)
    {
        var fragments = new BindingFragments();
        LocalFunctionBinder.Declare([new LocalFunctionDeclarationStatementStatementNode { LocalFunction = declaration }],
            fragments.Binding, fragments.Builder);
        return fragments;
    }
}