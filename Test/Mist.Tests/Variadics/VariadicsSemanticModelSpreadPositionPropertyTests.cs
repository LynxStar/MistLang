// Feature: variadics, task 13.4: property test for spread-accepting positions over the CLASSIC
// (legacy Mist.Semantics) pipeline.
//
// Property 17: Spread accepted in every designated position.
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**
//
// A Comptime Type[] spread is well-formed in each designated type position -- type-argument
// lists (9.1), field/member positions (9.2), base-type lists including implemented abstract
// types (9.3), and tuple-type element lists including tuple return types (9.4) -- with NO
// position-attributable diagnostic; a value-parameter declaration is NOT a spread position (9.5)
// because a value pack is declared with `params`, not a `...` suffix.
//
// Two observability regimes are exercised, matching the actual classic-pipeline behavior locked
// by the task 13.2 smoke tests (VariadicsSemanticModelSpreadPositionSmokeTests):
//   * ACCEPTANCE-ONLY (symbolic, deferred): a spread inside an open-generic type BODY (a
//     type-argument list, base-type list, or tuple type) carries the pack symbolically; the
//     concrete N-way materialization is deferred to backend monomorphization. For these the
//     property asserts acceptance -- no MIST_VAR_0002, no undefined base/abstract, no alias-arity
//     error -- NOT an eager element count.
//   * EXACT EXPANSION (eager, observable): a variadic ALIAS closed at a USE SITE expands the
//     spread in place into exactly one entry per pack element (zero for the empty pack). For
//     these the property asserts the exact resolved element count and ordered element names -- a
//     type-argument alias (`type Action<params TArgs> = Local<TArgs..., int>;`) and a tuple alias
//     (`type Row<params TArgs> = (int, TArgs...);`).
//
// Falsifiability: every acceptance case fails if a position-attributable diagnostic reappears;
// every use-site case fails if the spread stops expanding one-per-element (wrong count / wrong
// order) or if the empty pack expands to nonzero; the value-parameter case fails if the value
// pack is ever treated as an expanding type-side spread rather than the single-slot tuple
// (TArgs...). The field-position generator reuses MistVariadicsGenerator.GenTypeSpreadField.
//
// Everything runs entirely in-process against SemanticModelBuilder: ZERO external process spawns
// (no dotnet run, no compiled DLL, no stdlib).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated spread-position scenario: a complete, parseable Mist source that places a
/// <c>Type[]</c> spread in one designated position (or declares a value pack parameter for the
/// negative case). <see cref="ExpectedElements"/> carries the expected resolved element names for
/// the use-site categories that expand eagerly, and is empty for the acceptance-only and
/// value-parameter categories.
/// </summary>
public sealed record SpreadPositionScenario(
    string Kind,
    string Source,
    IReadOnlyList<string> ExpectedElements,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 17 (Spread accepted in every designated position)</b> over the
/// classic (legacy <c>Mist.Semantics</c>) pipeline. Builds the semantic model in-process and
/// asserts that a spread is accepted in every designated type position with no position-
/// attributable diagnostic, that a variadic alias closed at a use site expands the spread into
/// exactly one entry per pack element (zero for the empty pack), and that a value-parameter
/// position is not a spread position. <b>Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5.</b>
/// </summary>
public sealed class VariadicsSemanticModelSpreadPositionPropertyTests
{
    private const string PackInTypePositionId = "MIST_VAR_0002";
    private const string UndefinedBaseTypeId = "MIST_UNDEFINED_BASE_TYPE";
    private const string UndefinedAbstractId = "MIST_UNDEFINED_ABSTRACT";
    private const string AliasArityId = "MIST_SB_0063";

    private const string TypeArgSymbolicKind = "typearg-symbolic";
    private const string FieldSymbolicKind = "field-symbolic";
    private const string BaseTypeSymbolicKind = "basetype-symbolic";
    private const string TupleSymbolicKind = "tuple-symbolic";
    private const string TypeArgAliasUseSiteKind = "typearg-alias-usesite";
    private const string TupleAliasUseSiteKind = "tuple-alias-usesite";
    private const string ValueParamNegativeKind = "value-param-negative";

    private static readonly string[] PackNames = ["TArgs", "TMid", "TElems", "TValues", "TShapes"];

    // Element types are drawn from primitives whose resolution and DisplayName are stable without
    // the stdlib loaded (locked by the task 13.2 smoke tests that resolve int/bool/char use sites).
    private static readonly string[] ElementTypes = ["int", "bool", "char"];

    // =====================================================================
    // Property 17: a spread is accepted in every designated position (acceptance-only for the
    // symbolic body positions; exact one-per-element expansion at variadic alias use sites), and
    // a value-parameter position is not a spread position.
    // **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**
    // =====================================================================

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelSpreadPositionPropertyTests) }, MaxTest = 200)]
    public bool Spread_IsAcceptedInEveryDesignatedPosition_OverLegacyModel(SpreadPositionScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        if(scenario.Kind == TypeArgAliasUseSiteKind)
        {
            // A variadic alias closed at a use site expands the spread in place into exactly one
            // type-argument per pack element -- zero for the empty pack (9.1 + Property 5).
            AssertNoSpreadPositionDiagnostics(diagnostics);
            var elements = PackElementsOf(FieldType(model, "C", "Field"));
            Assert.Equal(scenario.ExpectedElements.Count, elements.Count);
            Assert.Equal(
                scenario.ExpectedElements.ToArray(),
                elements.Select(t => t.DisplayName).ToArray());
            return true;
        }

        if(scenario.Kind == TupleAliasUseSiteKind)
        {
            // A variadic tuple alias closed at a use site expands the spread into exactly one tuple
            // element per pack element, leaving only the fixed elements for an empty pack (9.4 +
            // Property 5).
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == AliasArityId);
            var tuple = Assert.IsType<TupleType>(FieldType(model, "C", "Field"));
            Assert.Equal(
                scenario.ExpectedElements.ToArray(),
                tuple.ElementTypes.Select(t => t.DisplayName).ToArray());
            return true;
        }

        if(scenario.Kind == ValueParamNegativeKind)
        {
            // A value pack parameter is declared with `params`, NOT a `...` spread, so it is not a
            // spread position (9.5): it names the pack to form the single-slot tuple (TArgs...) and
            // must not be flagged as a bare type position, nor expanded like a type-side spread.
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
            var parameter = Assert.Single(ParametersOf(model, "Use"));
            var tuple = Assert.IsType<TupleType>(parameter.DeclaredType);
            var element = Assert.Single(tuple.ElementTypes);
            Assert.True(Assert.IsType<TypeParameterType>(element).IsPack);
            return true;
        }

        // The remaining kinds are symbolic (open-generic body) spreads: accepted in place with NO
        // position-attributable diagnostic; the concrete N-way materialization is deferred to
        // backend monomorphization (9.1, 9.2, 9.3, 9.4).
        Assert.True(
            scenario.Kind is TypeArgSymbolicKind or FieldSymbolicKind
                or BaseTypeSymbolicKind or TupleSymbolicKind,
            "Unexpected scenario kind: " + scenario.Kind);
        AssertNoSpreadPositionDiagnostics(diagnostics);
        return true;
    }

    // =====================================================================
    // Generators
    // =====================================================================

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<SpreadPositionScenario> Scenarios()
    {
        return Gen.OneOf(
            GenTypeArgSymbolic(),
            GenFieldSymbolic(),
            GenBaseTypeSymbolic(),
            GenTupleSymbolic(),
            GenTypeArgAliasUseSite(),
            GenTupleAliasUseSite(),
            GenValueParamNegative()).ToArbitrary();
    }

    // ---- Acceptance-only (symbolic body positions) ----

    private static Gen<SpreadPositionScenario> GenTypeArgSymbolic()
    {
        return
            from pack in Gen.Elements(PackNames)
            from trailing in Gen.Elements(true, false)
            let source = trailing
                ? "public class Two<params " + pack + ", TResult> { }\n"
                    + "public class Use<params " + pack + ">\n{\n    public Two<" + pack + "..., int> Field;\n}"
                : "public class Holder<params " + pack + "> { }\n"
                    + "public class Use<params " + pack + ">\n{\n    public Holder<" + pack + "...> Field;\n}"
            select new SpreadPositionScenario(TypeArgSymbolicKind, source, [], "typearg-symbolic: " + source);
    }

    // Reuses the shared MistVariadicsGenerator field-spread generator (task 1.3) rather than
    // authoring a parallel one.
    private static Gen<SpreadPositionScenario> GenFieldSymbolic()
    {
        return
            from generated in MistVariadicsGenerator.GenTypeSpreadField()
            select new SpreadPositionScenario(
                FieldSymbolicKind, generated.Source, [], "field-symbolic: " + generated.Source);
    }

    private static Gen<SpreadPositionScenario> GenBaseTypeSymbolic()
    {
        return
            from pack in Gen.Elements(PackNames)
            from variant in Gen.Choose(0, 3)
            let source = BuildBaseTypeSource(pack, variant)
            select new SpreadPositionScenario(BaseTypeSymbolicKind, source, [], "basetype-symbolic: " + source);
    }

    private static Gen<SpreadPositionScenario> GenTupleSymbolic()
    {
        return
            from pack in Gen.Elements(PackNames)
            from lone in Gen.Elements(true, false)
            let ret = lone ? "(" + pack + "...)" : "(int, " + pack + "...)"
            let source = "public class Bundle<params " + pack + ">\n{\n    public " + ret + " Get() { }\n}"
            select new SpreadPositionScenario(TupleSymbolicKind, source, [], "tuple-symbolic: " + source);
    }

    // ---- Exact expansion (variadic alias use sites) ----

    private static Gen<SpreadPositionScenario> GenTypeArgAliasUseSite()
    {
        return
            from count in Gen.Choose(0, 4)
            from elements in Gen.ArrayOf(Gen.Elements(ElementTypes), count)
            let useSite = count == 0 ? "Action<[]>" : "Action<" + string.Join(", ", elements) + ">"
            let source =
                "public class Local<params TArgs, TResult> { }\n"
                + "type Action<params TArgs> = Local<TArgs..., int>;\n"
                + "public class C\n{\n    public " + useSite + " Field;\n}"
            select new SpreadPositionScenario(
                TypeArgAliasUseSiteKind, source, elements, "typearg-alias-usesite(" + count + "): " + useSite);
    }

    private static Gen<SpreadPositionScenario> GenTupleAliasUseSite()
    {
        return
            from count in Gen.Choose(0, 4)
            from elements in Gen.ArrayOf(Gen.Elements(ElementTypes), count)
            let useSite = count == 0 ? "Row<[]>" : "Row<" + string.Join(", ", elements) + ">"
            let expected = Prepend("int", elements)
            let source =
                "type Row<params TArgs> = (int, TArgs...);\n"
                + "public class C\n{\n    public " + useSite + " Field;\n}"
            select new SpreadPositionScenario(
                TupleAliasUseSiteKind, source, expected, "tuple-alias-usesite(" + count + "): " + useSite);
    }

    // ---- Negative (value parameter is not a spread position) ----

    private static Gen<SpreadPositionScenario> GenValueParamNegative()
    {
        return
            from pack in Gen.Elements(PackNames)
            let source =
                "public class Bundle<params " + pack + ">\n{\n    public void Use(params " + pack + " args) { }\n}"
            select new SpreadPositionScenario(ValueParamNegativeKind, source, [], "value-param-negative: " + source);
    }

    private static string BuildBaseTypeSource(string pack, int variant)
    {
        return variant switch
        {
            0 => "public class Foo<params " + pack + "> : " + pack + "... { }",
            1 => "public class BaseClass { }\n"
                + "public class Foo<params " + pack + "> : BaseClass, " + pack + "... { }",
            2 => "public abstract IThing { }\n"
                + "public class Foo<params " + pack + "> : IThing, " + pack + "... { }",
            _ => "public abstract IThing { }\n"
                + "public struct Foo<params " + pack + "> : IThing, " + pack + "... { }",
        };
    }

    private static string[] Prepend(string head, string[] tail)
    {
        var result = new string[tail.Length + 1];
        result[0] = head;
        Array.Copy(tail, 0, result, 1, tail.Length);
        return result;
    }

    // =====================================================================
    // Assertion + model-inspection helpers (mirroring the task 13.2 smoke tests).
    // =====================================================================

    /// <summary>
    /// Asserts that none of the diagnostics attributable to a spread's position were emitted: the
    /// bare-type-position error (<c>MIST_VAR_0002</c>), the undefined base/abstract errors, and the
    /// alias-arity error. "Position-attributable" is the key phrase in Requirements 9.1-9.4.
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

    // =====================================================================
    // In-process classic semantic-model construction (ZERO external process spawns).
    // =====================================================================

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Scenario source is expected to parse without lex/parse errors: <{source}>");

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