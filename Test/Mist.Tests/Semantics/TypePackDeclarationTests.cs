using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: which declared type parameters are packs, and which of them the use site may supply
/// bracket-free.
/// <para>
/// THE TWO SPELLINGS ARE NOT INTERCHANGEABLE AT THE POINT OF DECLARATION even though they mean the
/// same thing, and that asymmetry is the whole of what is asserted here. `params TArgs` is always
/// bracket-free; `Type[]:TArgs` is bracket-free only while it is the list's only pack, so adding a
/// second pack silently changes how the FIRST one is written at every use site.
/// </para>
/// </summary>
public sealed class TypePackDeclarationTests : ProgramModelProbe
{
    public TypePackDeclarationTests()
        : base("MistTypePacks_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private (TypePackKind Kind, int? Length) Pack(string declaration, string typeName, int ordinal)
    {
        var model = BuildModel(statement: "", extraTypes: declaration, extraPreamble: "", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<IHasTypeParameters>(model.TypesByFQN[$"Probe.{typeName}"]);
        var typeParam = type.TypeParametersList[ordinal];

        return (typeParam.PackKind, typeParam.PackLength);
    }

    [Fact]
    public void ParamsSpelling_IsAnUnbracketedPack()
    {
        Assert.Equal(
            (TypePackKind.Unbracketed, null),
            Pack("public class F<params TArgs, TResult> { }", "F<,>", ordinal: 0));
    }

    [Fact]
    public void NonPackParameter_IsNotAPack()
    {
        Assert.Equal(
            (TypePackKind.None, null),
            Pack("public class F<params TArgs, TResult> { }", "F<,>", ordinal: 1));
    }

    [Fact]
    public void LoneExplicitPack_IsUnbracketedToo()
    {
        Assert.Equal(
            (TypePackKind.Unbracketed, null),
            Pack("public class G<Type[]:TArgs, TResult> { }", "G<,>", ordinal: 0));
    }

    [Fact]
    public void SecondPackDemotesBothToBracketed()
    {
        Assert.Equal(
            (TypePackKind.Bracketed, null),
            Pack("public class H<Type[]:A, Type[]:B> { }", "H<,>", ordinal: 0));
    }

    [Fact]
    public void FixedSizeDomain_PinsTheArity()
    {
        Assert.Equal(
            (TypePackKind.Unbracketed, 3),
            Pack("public class K<Type[3]:TArgs> { }", "K<>", ordinal: 0));
    }

    /// <summary>
    /// A pack named in expression position is the comptime Type[] it ranges over, so the size-
    /// agnostic operations are the ARRAY's and need no rule of their own. Asserted through .Length
    /// because that is the one the sample reaches for.
    /// </summary>
    [Fact]
    public void PackArity_IsReadableInExpressionPosition()
    {
        _ = BuildModel(
            statement: "",
            extraTypes: "public class F<params TArgs> { public int Count() { return TArgs.Length; } }",
            extraPreamble: "",
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// The negative that keeps the above from being a blanket admission of any member on a pack.
    /// </summary>
    [Fact]
    public void MemberThePackDomainDoesNotHave_IsStillRefused()
    {
        _ = BuildModel(
            statement: "",
            extraTypes: "public class G<params TArgs> { public int Count() { return TArgs.Nonsense; } }",
            extraPreamble: "",
            out var diagnostics);

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER"));
    }

    [Fact]
    public void TwoBracketFreePacks_AreRefused()
    {
        var model = BuildModel(
            statement: "", extraTypes: "public class Bad<params A, params B> { }", extraPreamble: "",
            out var diagnostics);

        var error = Assert.Single(Errors(diagnostics, "MIST_VAR_0001"));
        Assert.Contains("'B' is a second bracket-free pack after 'A'", error.Message);

        // The refusal is a verdict, not an abandonment: the second pack still lands as a bracketed
        // one so nothing downstream has to cope with two absorbing slots.
        var type = Assert.IsAssignableFrom<IHasTypeParameters>(model.TypesByFQN["Probe.Bad<,>"]);
        Assert.Equal(TypePackKind.Bracketed, type.TypeParametersList[1].PackKind);
    }
}