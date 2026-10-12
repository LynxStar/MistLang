// Tier 1: a type declaration's written modifiers decide its accessibility and its sealed flag independently, and an unwritten accessibility is its scope's default.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Enums;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class DeclaredAccessibilityAndSealedTests
{
    [Theory]
    [InlineData("class", "public", false, Accessibility.Public, false)]
    [InlineData("class", "private", true, Accessibility.Private, false)]
    [InlineData("class", "protected", true, Accessibility.Protected, false)]
    [InlineData("class", "internal", false, Accessibility.Internal, false)]
    [InlineData("class", "", false, Accessibility.Internal, false)]
    [InlineData("class", "", true, Accessibility.Private, false)]
    [InlineData("struct", "", false, Accessibility.Internal, false)]
    [InlineData("struct", "public", false, Accessibility.Public, false)]
    [InlineData("class", "public sealed", false, Accessibility.Public, true)]
    [InlineData("class", "internal sealed", false, Accessibility.Internal, true)]
    [InlineData("struct", "public sealed", false, Accessibility.Public, true)]
    [InlineData("struct", "sealed", true, Accessibility.Private, true)]
    public void WrittenModifiersDecideAccessibilityAndSealedIndependently(
        string keyword, string written, bool nested, Accessibility accessibility, bool isSealed)
    {
        var diagnostics = new DiagnosticBag();
        var node = BindingFragments.At<TypeBodyDeclarationNode>();
        node.Keyword = keyword == "struct" ? new StructKeywordTypeKeywordNode() : new ClassKeywordTypeKeywordNode();
        node.Modifiers = written.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Modifier).ToImmutableArray();

        var modifiers = ModifierBuilder.Build(node, diagnostics, nested ? HandBuiltModel.Class("Outer") : null);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal((accessibility, isSealed), (modifiers.Accessibility, modifiers.IsSealed));
    }

    private static ModifierNode Modifier(string written)
    {
        return written switch
        {
            "public" => new PublicModifierModifierNode(),
            "private" => new PrivateModifierModifierNode(),
            "protected" => new ProtectedModifierModifierNode(),
            "internal" => new InternalModifierModifierNode(),
            "sealed" => new SealedModifierModifierNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(written), written, "Not a modifier this test spells."),
        };
    }
}