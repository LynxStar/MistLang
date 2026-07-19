// Feature: variadics - Pack type-parameter lists accepted uniformly on every generic-capable
// declaration kind over the LEGACY Mist.Semantics model, plus variadic newtype member cloning
// (Task 10.5). Requirements: 12.1, 12.4, 12.5, 22.1, 22.3.
//
// Task 10.5 closes the last declaration-kind gap in the classic (emitting) pipeline: the
// SemanticModelBuilder must accept a pack type-parameter list on a class, a struct, an
// abstract type, a type alias, AND a newtype, as well as on a method type-parameter list,
// applying the SAME pack rules uniformly (classification, inverse sugar, at most one
// Unbracketed_Pack, arity-in-slots) and emitting MIST_VAR_0001 for a method list with more
// than one Unbracketed_Pack. A newtype additionally clones the (possibly variadic) source
// type's members.
//
// Every declaration kind routes its type-parameter list through the single shared chokepoint
// SemanticModelBuilder.ExtractTypeParameters, so classification and the MIST_VAR_0001
// duplicate-`params` diagnostic are identical everywhere. Class/struct/abstract/alias/method
// were already wired to that helper (tasks 9.x); the newtype path did NOT extract a
// type-parameter list at all until this task, so a newtype pack was previously never
// classified over the legacy model (see the note in VariadicsSemanticModelPackClassificationTests).
//
// This suite drives SemanticModelBuilder entirely in-process (ZERO external process spawns:
// no dotnet run, no compiled DLL, no stdlib) and asserts, over the classic model:
//   - a pack is classified (IsPack / IsUnbracketedPack / PackFixedArity) on class, struct,
//     abstract type, type alias, and newtype declarations (R22.1, R12.1);
//   - a method type-parameter list with a pack is accepted and classified (R12.1);
//   - inverse sugar and fixed-size domains apply to a newtype list exactly as elsewhere;
//   - a method list AND a newtype list with two `params` packs each raise MIST_VAR_0001
//     (R12.4, R12.5, R3.5) -- proving the rule is uniform, not per-kind;
//   - a newtype over a variadic source type clones the source's members, including the
//     pack-typed spread-generated aggregate member (R22.3).
//
// A newtype over a variadic source at a CONCRETE arity (for example `new Bundle<int, string>`)
// depends on the flat-to-bracketed Binding_Rule, which is a type-checker concern (task 12) and
// is intentionally out of scope for the semantic-model builder; this suite therefore clones
// from a variadic source declared with its pack still symbolic.

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
/// Unit tests proving the legacy <see cref="SemanticModelBuilder"/> accepts a pack
/// type-parameter list on every generic-capable declaration kind (class, struct, abstract
/// type, type alias, newtype) and on method type-parameter lists, applying the shared pack
/// rules uniformly, and that a variadic newtype clones its source type's members
/// (Requirements 12.1, 12.4, 12.5, 22.1, 22.3). The newtype cases are newly enabled by task
/// 10.5; the class/struct/abstract/alias/method cases confirm the rules stay uniform across
/// the kinds that route through the same <c>ExtractTypeParameters</c> chokepoint.
/// </summary>
public sealed class VariadicsSemanticModelDeclarationKindPackTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // =====================================================================
    // Requirement 22.1 / 12.1: a pack type-parameter list is accepted and
    // classified on every generic-capable declaration kind. The pack routes
    // through the shared ExtractTypeParameters, so IsPack / IsUnbracketedPack
    // are recorded identically regardless of the enclosing declaration kind.
    // =====================================================================

    [Fact]
    public void PackList_OnClass_IsAcceptedAndClassified()
    {
        var (model, diagnostics) = BuildModel("public class Bundle<params TArgs> { }");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(TypeParametersOf(model, "Bundle")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void PackList_OnStruct_IsAcceptedAndClassified()
    {
        var (model, diagnostics) = BuildModel("public struct Row<params TArgs> { }");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(TypeParametersOf(model, "Row")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void PackList_OnAbstractType_IsAcceptedAndClassified()
    {
        var (model, diagnostics) = BuildModel("abstract ICountable<params TArgs> { int Count(); }");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(TypeParametersOf(model, "ICountable")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void PackList_OnTypeAlias_IsAcceptedAndClassified()
    {
        var (model, diagnostics) = BuildModel("type Aliased<params TArgs> = int;");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(AliasTypeParametersOf(model, "Aliased")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // The newly enabled kind: before task 10.5 the newtype path never extracted a
    // type-parameter list, so a newtype pack was not classified over the legacy model.
    [Fact]
    public void PackList_OnNewtype_IsAcceptedAndClassified()
    {
        var (model, diagnostics) = BuildModel("type Wrapped<params TArgs> = new int;");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(NewtypeTypeParametersOf(model, "Wrapped")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // =====================================================================
    // Requirement 3.2 / 3.6 on a newtype: inverse sugar promotes a lone explicit
    // `Type[]:` pack to Unbracketed, and a fixed-size `Type[N]:` domain records its
    // length -- exactly the behavior proven for class/struct elsewhere, now shown to
    // hold for the newtype path too (same shared helper).
    // =====================================================================

    [Fact]
    public void Newtype_LoneExplicitPack_IsUnbracketedByInverseSugar()
    {
        var (model, diagnostics) = BuildModel("type Wrapped<Type[]:TArgs> = new int;");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(NewtypeTypeParametersOf(model, "Wrapped")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    [Fact]
    public void Newtype_LoneFixedSizePack_RecordsLength()
    {
        var (model, _) = BuildModel("type Wrapped<Type[2]:TArgs> = new int;");

        var pack = Assert.IsType<TypeParameterType>(Assert.Single(NewtypeTypeParametersOf(model, "Wrapped")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: 2);
    }

    [Fact]
    public void Newtype_RegularAndPackSiblings_ClassifyPerSlot()
    {
        // A regular scalar slot next to a lone pack: inverse sugar still promotes the pack,
        // and arity counts both slots (the pack counts as exactly one).
        var (model, diagnostics) = BuildModel("type Wrapped<TKey, Type[]:TArgs> = new int;");

        var typeParams = NewtypeTypeParametersOf(model, "Wrapped");
        Assert.Equal(2, typeParams.Count);
        AssertRegular(typeParams[0], "TKey");
        AssertPack(typeParams[1], "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // =====================================================================
    // Requirement 12.1: a method type-parameter list accepts and classifies a pack.
    // Method packs route through the same ExtractTypeParameters as type-level lists.
    // =====================================================================

    [Fact]
    public void MethodList_WithPack_IsAcceptedAndClassified()
    {
        var (model, diagnostics) = BuildModel(
            "public class Host { public int Solo<params TArgs>() { return 0; } }");

        var pack = Assert.IsType<TypeParameterType>(
            Assert.Single(MethodTypeParametersOf(model, "Host", "Solo")));
        AssertPack(pack, "TArgs", unbracketed: true, fixedArity: null);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // =====================================================================
    // Requirements 12.4 / 12.5: a method type-parameter list with more than one
    // Unbracketed_Pack raises MIST_VAR_0001. Both `params` packs are classified
    // Unbracketed; the second is the violation.
    // =====================================================================

    [Fact]
    public void MethodList_WithTwoParamsPacks_RaisesMultipleParamsPacks()
    {
        var (model, diagnostics) = BuildModel(
            "public class Host { public int Bad<params A, params B>() { return 0; } }");

        var typeParams = MethodTypeParametersOf(model, "Host", "Bad");
        Assert.Equal(2, typeParams.Count);
        Assert.Equal(2, CountUnbracketedPacks(typeParams));
        Assert.Contains(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // The same duplicate-`params` rule fires uniformly for a NEWTYPE list too, proving the
    // rule lives in the shared chokepoint rather than being re-implemented per kind.
    [Fact]
    public void NewtypeList_WithTwoParamsPacks_RaisesMultipleParamsPacks()
    {
        var (model, diagnostics) = BuildModel("type Bad<params A, params B> = new int;");

        var typeParams = NewtypeTypeParametersOf(model, "Bad");
        Assert.Equal(2, typeParams.Count);
        Assert.Equal(2, CountUnbracketedPacks(typeParams));
        Assert.Contains(diagnostics, d => d.Id.Value == MultipleParamsPacksId);
    }

    // =====================================================================
    // Requirement 22.3: a newtype clones the (possibly variadic) source type's members.
    // The source `Bundle<params TArgs>` carries a spread-generated aggregate member
    // `Item : (TArgs...)` (task 10.4) and an ordinary method `Count`; cloning brings both
    // into the newtype's own member scope, so the variadic (pack-typed) member comes along.
    // =====================================================================

    [Fact]
    public void Newtype_OverVariadicSource_ClonesSpreadAndPackTypedMembers()
    {
        var (model, diagnostics) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Count() { return 0; }\n"
            + "}\n"
            + "\n"
            + "type BundleNewtype = new Bundle;");

        // The variadic source resolved and cloned without error.
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var members = NewtypeMembers(model, "BundleNewtype");

        // The spread-generated aggregate member came along: a field named `Item` whose type
        // is the value-pack tuple (TArgs...), i.e. a TupleType whose single element is the
        // pack. This is the pack-typed member that proves a variadic source clones correctly.
        var item = Assert.Single(members, m => m.Name == "Item");
        Assert.Equal(SymbolKind.Field, item.Kind);
        var itemTuple = Assert.IsType<TupleType>(item.DeclaredType);
        var element = Assert.Single(itemTuple.ElementTypes);
        Assert.True(
            Assert.IsType<TypeParameterType>(element).IsPack,
            "The cloned aggregate member's tuple element should be the source type's pack.");

        // The ordinary method also cloned, confirming the full member set came across.
        var count = Assert.Single(members, m => m.Name == "Count");
        Assert.Equal(SymbolKind.Method, count.Kind);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the model
    /// together with every construction-stage diagnostic. Pack classification, the
    /// at-most-one-Unbracketed_Pack rule, arity, and newtype member cloning are all decided
    /// during model construction, so inspecting the model and the construction diagnostics is
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
    /// Returns the declared type parameters of the source-declared class, struct, or abstract
    /// type named <paramref name="typeName"/>.
    /// </summary>
    private static IReadOnlyList<IType> TypeParametersOf(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == typeName && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract);
        Assert.NotNull(symbol);

        return symbol!.DeclaredType switch
        {
            ClassType classType => classType.TypeParameters,
            StructType structType => structType.TypeParameters,
            AbstractType abstractType => abstractType.TypeParameters,
            var other => throw new InvalidOperationException(
                $"Type '{typeName}' resolved to an unexpected declared type: {other?.GetType().Name ?? "null"}."),
        };
    }

    /// <summary>
    /// Returns the type parameters recorded on the source-declared type alias named
    /// <paramref name="aliasName"/> during construction.
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
    /// Returns the type parameters recorded on the source-declared newtype named
    /// <paramref name="newtypeName"/> during construction. Before task 10.5 the newtype path
    /// did not extract a type-parameter list, so this list would have been empty.
    /// </summary>
    private static IReadOnlyList<IType> NewtypeTypeParametersOf(ISemanticModel model, string newtypeName)
    {
        var newtype = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == newtypeName && s.Kind == SymbolKind.Newtype);
        Assert.NotNull(newtype);

        return newtype!.TypeParameters;
    }

    /// <summary>
    /// Returns the members cloned into the newtype named <paramref name="newtypeName"/>'s own
    /// member scope.
    /// </summary>
    private static IReadOnlyList<ISymbol> NewtypeMembers(ISemanticModel model, string newtypeName)
    {
        var newtype = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == newtypeName && s.Kind == SymbolKind.Newtype);
        Assert.NotNull(newtype);

        var newtypeType = Assert.IsType<NewtypeType>(newtype!.DeclaredType);
        return newtypeType.MemberScope.Members;
    }

    /// <summary>
    /// Returns the type parameters recorded on the method named <paramref name="methodName"/>
    /// declared on the type <paramref name="typeName"/>. The method's pack classification is
    /// stored on the method <see cref="Symbol"/> by <c>RegisterMethodTypeParameters</c>, which
    /// shares <c>ExtractTypeParameters</c> with the type-level path.
    /// </summary>
    private static IReadOnlyList<IType> MethodTypeParametersOf(
        ISemanticModel model, string typeName, string methodName)
    {
        var typeSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == typeName && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract);
        Assert.NotNull(typeSymbol);

        var memberScope = typeSymbol!.DeclaredType switch
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

    private static int CountUnbracketedPacks(IReadOnlyList<IType> typeParams)
    {
        return typeParams.Count(p => p is TypeParameterType { IsUnbracketedPack: true });
    }

    /// <summary>
    /// Asserts that <paramref name="slot"/> is a pack <see cref="TypeParameterType"/> with the
    /// expected name, bracket-freeness, and fixed-size domain length.
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