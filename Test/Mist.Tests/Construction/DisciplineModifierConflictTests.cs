using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

/// <summary>
/// <see cref="ProgramModelDiagnostics.ConflictingDisciplineModifiers"/>, drop-semantics Requirement 1.6.
/// </summary>
[Trait("Tier", "1")]
public sealed class DisciplineModifierConflictTests
{
    private const int DeclarationOffset = 30;

    [Theory]
    [InlineData("affine linear")]
    [InlineData("affine relevant")]
    [InlineData("linear relevant")]
    [InlineData("relevant public affine")]
    [InlineData("linear affine relevant")]
    public void MoreThanOneDisciplineIsRefusedOnceAtTheDeclaration(string written)
    {
        var diagnostics = new DiagnosticBag();

        _ = Build(written, diagnostics);

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0316", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(DeclarationOffset + 1, diagnostic.Position().Start.Column);
        Assert.Equal(
            "A declaration cannot have more than one substructural discipline modifier. Specify at most one of 'affine', 'linear', or 'relevant'.",
            diagnostic.Message);
    }

    [Theory]
    [InlineData("affine", SubstructuralDiscipline.Affine)]
    [InlineData("public linear", SubstructuralDiscipline.Linear)]
    [InlineData("relevant sealed", SubstructuralDiscipline.Relevant)]
    public void OneDisciplineIsAdmittedAsTheTypesDiscipline(string written, SubstructuralDiscipline expected)
    {
        var diagnostics = new DiagnosticBag();

        var modifiers = Build(written, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(expected, modifiers.Discipline);
    }

    private static MemberModifiers Build(string written, DiagnosticBag diagnostics)
    {
        var declaration = BindingFragments.At<TypeBodyDeclarationNode>(DeclarationOffset);
        declaration.Keyword = new ClassKeywordTypeKeywordNode();
        declaration.Name = BindingFragments.Token("Token");
        declaration.Modifiers = [.. written.Split(' ').Select(Modifier)];
        return ModifierBuilder.Build(declaration, diagnostics, declaringType: null);
    }

    private static ModifierNode Modifier(string keyword)
    {
        return keyword switch
        {
            "affine" => new AffineModifierModifierNode(),
            "linear" => new LinearModifierModifierNode(),
            "relevant" => new RelevantModifierModifierNode(),
            "public" => new PublicModifierModifierNode(),
            "sealed" => new SealedModifierModifierNode(),
            _ => throw new ArgumentException("Unknown modifier", nameof(keyword)),
        };
    }
}