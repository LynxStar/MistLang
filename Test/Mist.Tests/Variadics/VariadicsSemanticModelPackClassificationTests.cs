// Feature: variadics - Unit tests for pack classification and arity-in-slots over the
// LEGACY Mist.Semantics model (Task 9.5). Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6.
//
// This is the fuller, dedicated classification/arity unit suite promised by the two
// pre-existing smoke suites (task 9.1 representation, task 9.2 enforcement). It drives
// SemanticModelBuilder entirely in-process (ZERO external process spawns: no dotnet run,
// no compiled DLL) and inspects the pack flags the builder records on TypeParameterType
// (IsPack, IsUnbracketedPack, PackFixedArity), plus the MIST_VAR_0001 diagnostic.
//
// Classification, the at-most-one-Unbracketed_Pack rule, and arity all live in the shared
// SemanticModelBuilder.ExtractTypeParameters, which every generic-capable declaration kind
// routes through. This suite deliberately COMPLEMENTS the smoke tests instead of repeating
// them:
//   * VariadicsSemanticModelPackRepresentationSmokeTests (9.1) already covers the class
//     `params` -> Unbracketed, lone `Type[]:` -> Unbracketed, two `Type[]:` -> both
//     Bracketed, and a lone `Type[2]:` -> PackFixedArity 2 cases;
//   * VariadicsSemanticModelPackEnforcementSmokeTests (9.2) already covers the class
//     MIST_VAR_0001 mixed cases and asserts arity-5 by COUNT plus the Func arity-2 case.
//
// The additional coverage here is:
//   * the arity-5 example asserted PER SLOT (name + IsPack + IsUnbracketedPack +
//     PackFixedArity for every one of the 5 slots), not merely by count;
//   * inverse sugar with a regular (non-pack) sibling: `Pair<TKey, Type[]:TArgs>`;
//   * the `params A` + `Type[]:B` mix asserted per slot (one Unbracketed, one Bracketed);
//   * PackFixedArity at more than one N (2 and 3) and null for both `Type[]` and `params`,
//     including a fixed-size pack that stays Bracketed;
//   * classification on a METHOD type-parameter list (method packs route through the same
//     ExtractTypeParameters), including a method-list MIST_VAR_0001 duplicate-`params`;
//   * classification on an `abstract` type and a `type` alias declaration.
//
// Newtype declarations are intentionally NOT covered: the legacy builder's newtype
// registration does not extract a type-parameter list, so a newtype pack is not classified
// over this model yet (that belongs to task 10.5). The property test is task 9.4.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Unit tests for type-parameter pack classification and arity-in-slots over the legacy
/// <see cref="SemanticModelBuilder"/> (Requirements 3.1-3.6). Each test builds the model
/// in-process and inspects the <see cref="TypeParameterType"/> pack flags
/// (<see cref="TypeParameterType.IsPack"/>, <see cref="TypeParameterType.IsUnbracketedPack"/>,
/// <see cref="TypeParameterType.PackFixedArity"/>) and, for the multi-pack cases, the
/// <c>MIST_VAR_0001</c> diagnostic. These complement the representation (9.1) and
/// enforcement (9.2) smoke suites rather than repeating them.
/// </summary>
public sealed class VariadicsSemanticModelPackClassificationTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.6 (with 3.1/3.2/3.3): the design's arity-5 example asserted
    // PER SLOT. The enforcement smoke test only asserts the count (5) and the
    // number of Unbracketed packs (1); here every slot's name, pack-ness,
    // bracket-freeness, and fixed arity is pinned down.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ArityFiveExample_ClassifiesEverySlot_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel(
            "public class Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes> { }");

        var typeParams = TypeParametersOf(model, "Foo");

        // Arity is the count of declared slots, each pack counting as exactly one.
        Assert.Equal(5, typeParams.Count);

        // Slot 0: a regular scalar type parameter (not a pack).
        AssertRegular(typeParams[0], "TVal");

        // Slot 1: an explicit `Type[]:` pack that is NOT the lone pack (CtorArgs is also a
        // pack), so it stays Bracketed with an unknown-length domain.
        AssertPack(typeParams[1], "TArgs", unbracketed: false, fixedArity: null);

        // Slot 2: the single `params` pack -> always the Unbracketed pack; `params` is
        // sugar for the unknown-length `Type[]:`, so its fixed arity is null.
        AssertPack(typeParams[2], "CtorArgs", unbracketed: true, fixedArity: null);

        // Slot 3: a second explicit `Type[]:` pack -> Bracketed, unknown length.
        AssertPack(typeParams[3], "Args3", unbracketed: false, fixedArity: null);

        // Slot 4: another regular scalar type parameter.
        AssertRegular(typeParams[4], "TRes");

        // Exactly one Unbracketed pack -> no more-than-one-params violation.
        Assert.Equal(1, CountUnbracketedPacks(typeParams));
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.2: inverse sugar promotes the LONE pack even when it shares
    // the list with a regular (non-pack) sibling. The smoke suite only covers a
    // list whose single member is the pack; this pins down the mixed shape.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LonePackWithRegularSibling_IsUnbracketedByInverseSugar_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel("public class Pair<TKey, Type[]:TArgs> { }");

        var typeParams = TypeParametersOf(model, "Pair");
        Assert.Equal(2, typeParams.Count);

        // TKey is an ordinary scalar type parameter.
        AssertRegular(typeParams[0], "TKey");

        // TArgs is the only pack in the list, so inverse sugar makes it Unbracketed even
        // though it is not the only slot.
        AssertPack(typeParams[1], "TArgs", unbracketed: true, fixedArity: null);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 3.1 / 3.3: `params A` + `Type[]:B` classifies A as the single
    // Unbracketed pack and B as a Bracketed pack. The enforcement smoke test only
    // asserts this shape does NOT raise MIST_VAR_0001; here the per-slot kinds are
    // pinned down as well.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParamsPackPlusExplicitPack_IsOneUnbracketedOneBracketed_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel("public class Mixed<params A, Type[]:B> { }");

        var typeParams = TypeParametersOf(model, "Mixed");
        Assert.Equal(2, typeParams.Count);

        // `params A` is the single bracket-free pack.
        AssertPack(typeParams[0], "A", unbracketed: true, fixedArity: null);

        // B is a pack too, so A is not the lone pack; the explicit `Type[]:B` stays
        // Bracketed (inverse sugar only promotes a list's single pack).
        AssertPack(typeParams[1], "B", unbracketed: false, fixedArity: null);

        Assert.Equal(1, CountUnbracketedPacks(typeParams));
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PackFixedArity: `Type[N]` records its comptime length N (here 2 AND 4 in one
    // list), `Type[]` and `params` record null. The smoke suite only exercises a
    // single lone `Type[2]:` pack; this shows the fixed length is recorded even for
    // a Bracketed (non-lone) pack, alongside a null-length sibling.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FixedSizeBracketedPacks_RecordTheirLengths_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel("public class Mix<Type[]:A, Type[2]:B, Type[4]:C> { }");

        var typeParams = TypeParametersOf(model, "Mix");
        Assert.Equal(3, typeParams.Count);

        // Three explicit packs -> none is the lone pack, so all stay Bracketed. Fixed
        // arity is recorded independently of bracket-freeness: `Type[]` -> null,
        // `Type[2]` -> 2, `Type[4]` -> 4.
        AssertPack(typeParams[0], "A", unbracketed: false, fixedArity: null);
        AssertPack(typeParams[1], "B", unbracketed: false, fixedArity: 2);
        AssertPack(typeParams[2], "C", unbracketed: false, fixedArity: 4);

        // More than one explicit pack is permitted and none is Unbracketed.
        Assert.Equal(0, CountUnbracketedPacks(typeParams));
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void LoneFixedSizePack_OnStruct_IsUnbracketedAndRecordsLength_OverLegacyModel()
    {
        // A lone fixed-size `Type[3]:` pack (a different N than the smoke suite's 2) on a
        // struct: inverse sugar makes it Unbracketed and PackFixedArity records 3.
        var (model, _) = BuildModel("public struct Holder<Type[3]:T> { }");

        var typeParams = TypeParametersOf(model, "Holder");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        AssertPack(pack, "T", unbracketed: true, fixedArity: 3);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 3.1 / 3.2 / 3.6 on a METHOD type-parameter list. Method packs
    // route through the same ExtractTypeParameters, so the classification applies
    // uniformly. Not covered by the smoke suites (which only exercise type-level
    // lists).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParamsPack_IsClassifiedUnbracketed_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel(
            "public class Host { public int Solo<params TArgs>() { return 0; } }");

        var typeParams = MethodTypeParametersOf(model, "Host", "Solo");

        // One method type-parameter slot -> arity 1.
        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void MethodLoneExplicitPack_IsUnbracketedByInverseSugar_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel(
            "public class Host { public int Lone<Type[]:A>() { return 0; } }");

        var typeParams = MethodTypeParametersOf(model, "Host", "Lone");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        AssertPack(pack, "A", unbracketed: true, fixedArity: null);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 3.4 / 3.5 on a METHOD type-parameter list: two `params` packs
    // are two Unbracketed packs, so MIST_VAR_0001 fires for the method list too.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodList_WithTwoParamsPacks_ReportsMultipleParamsPacks_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel(
            "public class Host { public int Bad<params A, params B>() { return 0; } }");

        // Both `params` packs are classified Unbracketed on the method...
        var typeParams = MethodTypeParametersOf(model, "Host", "Bad");
        Assert.Equal(2, typeParams.Count);
        Assert.Equal(2, CountUnbracketedPacks(typeParams));

        // ...and the second one raises MIST_VAR_0001.
        Assert.Contains(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1 on an `abstract` type declaration (Mist's interface-like
    // construct). Not covered by the smoke suites.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractTypeParamsPack_IsClassifiedUnbracketed_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel(
            "abstract ICountable<params TArgs> { int Count(); }");

        var typeParams = TypeParametersOf(model, "ICountable");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 3.1 / 3.2 on a `type` alias declaration. Alias type-parameter
    // lists route through the same ExtractTypeParameters and store the classified
    // packs on the alias symbol. Not covered by the smoke suites.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeAliasParamsPack_IsClassifiedUnbracketed_OverLegacyModel()
    {
        var (model, diagnostics) = BuildModel("type Aliased<params TArgs> = int;");

        var typeParams = AliasTypeParametersOf(model, "Aliased");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(typeParams));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model together with every construction-stage diagnostic. Pack classification, the
    /// at-most-one-Unbracketed_Pack rule, and arity are all decided during model
    /// construction, so inspecting the result and the construction diagnostics is
    /// sufficient.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            "Unit-test source is expected to parse without lex/parse errors.");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Finds the single source-declared class, struct, or abstract type symbol named
    /// <paramref name="typeName"/> and returns its declared type parameters.
    /// </summary>
    private static IReadOnlyList<IType> TypeParametersOf(ISemanticModel model, string typeName)
    {
        var symbol = FindType(model, typeName);

        return symbol.DeclaredType switch
        {
            ClassType classType => classType.TypeParameters,
            StructType structType => structType.TypeParameters,
            AbstractType abstractType => abstractType.TypeParameters,
            var other => throw new InvalidOperationException(
                $"Type '{typeName}' resolved to an unexpected declared type: {other?.GetType().Name ?? "null"}."),
        };
    }

    /// <summary>
    /// Finds the method named <paramref name="methodName"/> declared on the type
    /// <paramref name="typeName"/> and returns its declared type parameters. The method's
    /// pack classification is stored on the method <see cref="Symbol"/> by
    /// <c>RegisterMethodTypeParameters</c>, which shares <c>ExtractTypeParameters</c> with
    /// the type-level path.
    /// </summary>
    private static IReadOnlyList<IType> MethodTypeParametersOf(
        ISemanticModel model, string typeName, string methodName)
    {
        var typeSymbol = FindType(model, typeName);

        var memberScope = typeSymbol.DeclaredType switch
        {
            ClassType classType => classType.MemberScope,
            StructType structType => structType.MemberScope,
            AbstractType abstractType => abstractType.MemberScope,
            var other => throw new InvalidOperationException(
                $"Type '{typeName}' resolved to an unexpected declared type: {other?.GetType().Name ?? "null"}."),
        };

        var method = memberScope.Members
            .OfType<Symbol>()
            .FirstOrDefault(m => m.Name == methodName && m.Kind == SymbolKind.Method);
        Assert.NotNull(method);

        return method!.TypeParameters;
    }

    /// <summary>
    /// Finds the source-declared type alias symbol named <paramref name="aliasName"/> and
    /// returns the type parameters recorded on it during construction.
    /// </summary>
    private static IReadOnlyList<IType> AliasTypeParametersOf(ISemanticModel model, string aliasName)
    {
        var alias = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == aliasName && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(alias);

        return alias!.TypeParameters;
    }

    /// <summary>
    /// Finds the single source-declared class, struct, or abstract type symbol named
    /// <paramref name="typeName"/>.
    /// </summary>
    private static ISymbol FindType(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == typeName && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract);
        Assert.NotNull(symbol);

        return symbol!;
    }

    private static int CountUnbracketedPacks(IReadOnlyList<IType> typeParams)
    {
        return typeParams.Count(p => p is TypeParameterType { IsUnbracketedPack: true });
    }

    /// <summary>
    /// Asserts that <paramref name="slot"/> is a pack <see cref="TypeParameterType"/> with
    /// the expected name, bracket-freeness, and fixed-size domain length.
    /// </summary>
    private static void AssertPack(IType slot, string name, bool unbracketed, int? fixedArity)
    {
        var pack = Assert.IsType<TypeParameterType>(slot);
        Assert.Equal(name, pack.Name);
        Assert.True(pack.IsPack);
        Assert.Equal(unbracketed, pack.IsUnbracketedPack);
        Assert.Equal(fixedArity, pack.PackFixedArity);
    }

    /// <summary>
    /// Asserts that <paramref name="slot"/> is an ordinary (non-pack) scalar
    /// <see cref="TypeParameterType"/> with the expected name.
    /// </summary>
    private static void AssertRegular(IType slot, string name)
    {
        var tp = Assert.IsType<TypeParameterType>(slot);
        Assert.Equal(name, tp.Name);
        Assert.False(tp.IsPack);
        Assert.False(tp.IsUnbracketedPack);
        Assert.Null(tp.PackFixedArity);
    }
}