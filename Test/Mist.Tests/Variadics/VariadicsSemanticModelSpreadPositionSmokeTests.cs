// Feature: variadics, task 13.2: type-side spread expansion in every designated position over the
// CLASSIC (legacy Mist.Semantics) pipeline. Requirements: 6.8, 8.1, 9.1, 9.2, 9.3, 9.4, 9.5.
//
// A Comptime Type[] spread `TArgs...` expands IN PLACE into one entry per pack element (zero for
// an empty pack) in:
//   - a type-argument list        (9.1, e.g. `Func<TArgs..., void>` / `Local<TArgs..., int>`)
//   - a field/member type position (9.2, e.g. `public TArgs... Item;` -- Requirement 10 owns the
//                                   member-family GENERATION; here we only assert the spread is
//                                   accepted without a position-attributable diagnostic)
//   - a base-type list            (9.3, including implemented abstract types, e.g. `: TArgs...`)
//   - a tuple-type element list   (9.4, including tuple return types, e.g. `(TArgs...)`)
// A value-PARAMETER declaration is NOT a spread position (9.5): a value pack is declared with
// `params` (`params TArgs args`), not a `...` suffix.
//
// While the operand pack is symbolic (an open generic body) the spread carries the pack itself as
// a single placeholder entry and the N-way materialization is deferred to backend monomorphization
// -- exactly the deferral used by spread-generated members (Requirement 10) and the value-pack
// tuple `(TArgs...)`. When a variadic alias is CLOSED at a use site (completing task 12.1's
// deferred work) the spread is expanded in place into the bound element types (zero for an empty
// pack), which these tests verify directly.
//
// Everything runs entirely in-process against SemanticModelBuilder (ZERO external process spawns:
// no dotnet run, no compiled DLL, no stdlib) and asserts on the emitted diagnostics and the
// resolved types. This is the classic-track verification for task 13.2; the property test
// (task 13.4) and the dedicated unit suite (task 13.5) exercise these rules more broadly.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests asserting the classic <see cref="SemanticModelBuilder"/> accepts a type-side
/// <c>Type[]</c> spread in every designated position without a position-attributable diagnostic,
/// and -- when a variadic alias is closed at a use site -- expands it in place into one entry per
/// pack element (zero for an empty pack). Requirements 6.8, 8.1, 9.1, 9.2, 9.3, 9.4, 9.5.
/// </summary>
public sealed class VariadicsSemanticModelSpreadPositionSmokeTests
{
    private const string PackInTypePositionId = "MIST_VAR_0002";
    private const string UndefinedBaseTypeId = "MIST_UNDEFINED_BASE_TYPE";
    private const string UndefinedAbstractId = "MIST_UNDEFINED_ABSTRACT";
    private const string AliasArityId = "MIST_SB_0063";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.1: a spread in a type-argument list is well-formed with no
    // position-attributable diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpreadInTypeArgumentList_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public class Holder<params TArgs> { }\n"
            + "public class Use<params TArgs>\n"
            + "{\n"
            + "    public Holder<TArgs...> Field;\n"
            + "}");

        AssertNoSpreadPositionDiagnostics(diagnostics);
    }

    [Fact]
    public void SpreadInTypeArgumentListWithTrailingScalar_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public class Two<params TArgs, TResult> { }\n"
            + "public class Use<params TArgs>\n"
            + "{\n"
            + "    public Two<TArgs..., int> Field;\n"
            + "}");

        AssertNoSpreadPositionDiagnostics(diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.1 + 12.1: a variadic alias whose body spreads the pack into a type-argument
    // list is closed at a use site by folding the flat arguments into the pack and expanding the
    // spread in place -- so `Action<int, string>` for `type Action<params TArgs> =
    // Local<TArgs..., int>;` yields `Local` with the pack bound to exactly the supplied elements,
    // and no MIST_SB_0063 arity error.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VariadicAliasUseSite_ExpandsSpreadIntoOneTypeArgumentPerElement()
    {
        var (model, diagnostics) = BuildModel(
            "public class Local<params TArgs, TResult> { }\n"
            + "type Action<params TArgs> = Local<TArgs..., int>;\n"
            + "public class C\n"
            + "{\n"
            + "    public Action<bool, char> Field;\n"
            + "}");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == AliasArityId);
        AssertNoSpreadPositionDiagnostics(diagnostics);

        // Action<bool, char> closes to Local with the pack bound to exactly [bool, char].
        var packElements = PackElementsOf(FieldType(model, "C", "Field"));
        Assert.Equal(2, packElements.Count);
        Assert.Equal("bool", packElements[0].DisplayName);
        Assert.Equal("char", packElements[1].DisplayName);
    }

    [Fact]
    public void VariadicAliasUseSite_ThreeElements_ExpandsToThreeTypeArguments()
    {
        var (model, _) = BuildModel(
            "public class Local<params TArgs, TResult> { }\n"
            + "type Action<params TArgs> = Local<TArgs..., int>;\n"
            + "public class C\n"
            + "{\n"
            + "    public Action<int, bool, char> Field;\n"
            + "}");

        var packElements = PackElementsOf(FieldType(model, "C", "Field"));
        Assert.Equal(3, packElements.Count);
        Assert.Equal(new[] { "int", "bool", "char" }, packElements.Select(t => t.DisplayName).ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.8 / 8.1: an EMPTY pack spread expands to ZERO entries.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VariadicAliasUseSite_EmptyPack_ExpandsToZeroTypeArguments()
    {
        var (model, diagnostics) = BuildModel(
            "public class Local<params TArgs, TResult> { }\n"
            + "type Action<params TArgs> = Local<TArgs..., int>;\n"
            + "public class C\n"
            + "{\n"
            + "    public Action<[]> Field;\n"
            + "}");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == AliasArityId);

        // The empty pack contributes zero type-arguments to Local's pack slot.
        var packElements = PackElementsOf(FieldType(model, "C", "Field"));
        Assert.Empty(packElements);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.2: a spread in a field/member type position is accepted without a
    // position-attributable diagnostic. (Requirement 10 owns the member-family generation; this
    // only asserts the spread is not misreported as a bare-type-position error.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpreadInFieldPosition_IsAcceptedWithoutBareTypeDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.3: a spread in a base-type list (including implemented abstract types) is
    // accepted without a position-attributable diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpreadInBaseTypeList_Bare_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public class Foo<params TArgs> : TArgs... { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedBaseTypeId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedAbstractId);
    }

    [Fact]
    public void SpreadInBaseTypeList_AfterBaseClass_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public class BaseClass { }\n"
            + "public class Foo<params TArgs> : BaseClass, TArgs... { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedBaseTypeId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedAbstractId);
    }

    [Fact]
    public void SpreadInBaseTypeList_AfterImplementedAbstract_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public abstract IThing { }\n"
            + "public class Foo<params TArgs> : IThing, TArgs... { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedBaseTypeId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedAbstractId);
    }

    [Fact]
    public void SpreadInStructBaseTypeList_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public abstract IThing { }\n"
            + "public struct Foo<params TArgs> : IThing, TArgs... { }");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedBaseTypeId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedAbstractId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.4: a spread in a tuple-type element list (including a tuple return type) is
    // well-formed; the lone `(TArgs...)` symbolic form carries the pack as its single element.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpreadInTupleReturnType_LoneSpread_IsAcceptedAsValuePackTuple()
    {
        var (model, diagnostics) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public (TArgs...) Get() { }\n"
            + "}");

        AssertNoSpreadPositionDiagnostics(diagnostics);

        // The lone `(TArgs...)` return type is the value-pack tuple: while symbolic it carries the
        // pack as its single element (one placeholder entry, deferred).
        var tuple = Assert.IsType<TupleType>(ReturnTypeOf(model, "Bundle", "Get"));
        var element = Assert.Single(tuple.ElementTypes);
        var pack = Assert.IsType<TypeParameterType>(element);
        Assert.True(pack.IsPack);
    }

    [Fact]
    public void SpreadInTupleElementList_MultiElement_IsAcceptedWithoutDiagnostic()
    {
        var (_, diagnostics) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public (int, TArgs...) Get() { }\n"
            + "}");

        AssertNoSpreadPositionDiagnostics(diagnostics);
    }

    [Fact]
    public void VariadicTupleAliasUseSite_ExpandsSpreadIntoOneTupleElementPerElement()
    {
        var (model, diagnostics) = BuildModel(
            "type Row<params TArgs> = (int, TArgs...);\n"
            + "public class C\n"
            + "{\n"
            + "    public Row<bool, char> Field;\n"
            + "}");

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == AliasArityId);

        // Row<bool, char> closes `(int, TArgs...)` to the tuple (int, bool, char): the spread
        // expanded in place into one element per pack element.
        var tuple = Assert.IsType<TupleType>(FieldType(model, "C", "Field"));
        Assert.Equal(new[] { "int", "bool", "char" }, tuple.ElementTypes.Select(t => t.DisplayName).ToArray());
    }

    [Fact]
    public void VariadicTupleAliasUseSite_EmptyPack_ExpandsToOnlyFixedElements()
    {
        var (model, _) = BuildModel(
            "type Row<params TArgs> = (int, TArgs...);\n"
            + "public class C\n"
            + "{\n"
            + "    public Row<[]> Field;\n"
            + "}");

        // The empty pack contributes zero tuple elements, leaving only the fixed `int`.
        var tuple = Assert.IsType<TupleType>(FieldType(model, "C", "Field"));
        var element = Assert.Single(tuple.ElementTypes);
        Assert.Equal("int", element.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 9.5: a value-parameter declaration is NOT a spread position -- a value pack is
    // declared with `params` (`params TArgs args`), not a `...` suffix. The value pack parameter
    // names the pack to form its tuple (TArgs...) and must NOT be flagged as a bare type position,
    // and the parameter type is the single-slot value-pack tuple, not an expanded spread.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValuePackParameter_IsNotATypeSideSpreadPosition()
    {
        var (model, diagnostics) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void Use(params TArgs args) { }\n"
            + "}");

        // The value pack parameter is accepted (params is the marker, not `...`).
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);

        // Its type is the single-slot value-pack tuple (TArgs...), i.e. one pack element -- it is
        // NOT expanded like a type-side spread.
        var param = Assert.Single(ParametersOf(model, "Use"));
        var tuple = Assert.IsType<TupleType>(param.DeclaredType);
        var element = Assert.Single(tuple.ElementTypes);
        Assert.True(Assert.IsType<TypeParameterType>(element).IsPack);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers (ZERO external process spawns).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Asserts that none of the diagnostics attributable to a spread's position were emitted: the
    /// bare-type-position error (<c>MIST_VAR_0002</c>), the undefined base/abstract errors, and the
    /// alias-arity error. Position-attributable is the key phrase in Requirements 9.1-9.4.
    /// </summary>
    private static void AssertNoSpreadPositionDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedBaseTypeId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedAbstractId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == AliasArityId);
    }

    /// <summary>The pack element list bound to the (single) pack slot of a constructed variadic type.</summary>
    private static IReadOnlyList<IType> PackElementsOf(IType? type)
    {
        var constructed = Assert.IsType<ConstructedType>(type);
        Assert.NotNull(constructed.DependentArguments);
        var packArg = constructed.DependentArguments!.FirstOrDefault(a => a is { IsPack: true });
        Assert.NotNull(packArg);
        return packArg!.PackElementTypes!;
    }

    /// <summary>The declared type of a field member on a named type.</summary>
    private static IType? FieldType(ISemanticModel model, string typeName, string fieldName)
    {
        var type = FindType<ClassType>(model, typeName);
        var member = type.MemberScope.Lookup(fieldName);
        Assert.NotNull(member);
        return member!.DeclaredType;
    }

    /// <summary>The declared return type of a method member on a named type.</summary>
    private static IType? ReturnTypeOf(ISemanticModel model, string typeName, string methodName)
    {
        var type = FindType<ClassType>(model, typeName);
        var member = type.MemberScope.Lookup(methodName);
        Assert.NotNull(member);
        return member!.DeclaredType;
    }

    private static T FindType<T>(ISemanticModel model, string name)
        where T : class, IType
    {
        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == name && s.DeclaredType is T);
        Assert.NotNull(symbol);
        return (T)symbol!.DeclaredType!;
    }

    /// <summary>
    /// Returns the declared parameter <see cref="Symbol"/> instances (in declaration order) of the
    /// single source method named <paramref name="methodName"/>, read from the declaring AST node.
    /// </summary>
    private static IReadOnlyList<Symbol> ParametersOf(ISemanticModel model, string methodName)
    {
        var methodSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == methodName && s.Kind == SymbolKind.Method);
        Assert.NotNull(methodSymbol);

        var paramList = model.GetDeclaringNode(methodSymbol!) is MethodDeclarationNode method
            ? method.Parameters
            : null;

        var result = new List<Symbol>();
        if(paramList is null)
        {
            return result;
        }

        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode && model.GetDeclaredSymbol(paramNode) is Symbol sym)
            {
                result.Add(sym);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely in-process
    /// (no stdlib, no analyzers, no emit), returning the model and every diagnostic collected
    /// during construction. Type-side spread acceptance and variadic-alias expansion run during
    /// model construction, so inspecting the builder's output is sufficient for these smoke checks.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Smoke-test source is expected to parse without lex/parse errors: <{source}>");

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
}