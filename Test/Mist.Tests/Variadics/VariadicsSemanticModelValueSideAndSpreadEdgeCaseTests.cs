// Feature: variadics - Consolidated unit tests for value-side representation and
// spread-member edge cases over the CLASSIC (legacy Mist.Semantics) model (Task 10.9).
// Requirements: 7.1, 7.7, 10.4, 10.5, 10.7.
//
// This class is the named 10.9 deliverable: a single, cohesive edge-case suite over the
// legacy model that mirrors the ProgramModel-track edge-case suite
// (VariadicsValueSideAndSpreadMemberEdgeCaseTests, task 5.9). It consolidates the five
// edges Task 10.9 calls out and adds the identity detail the task-10.1/10.4 SMOKE tests
// (VariadicsSemanticModelValuePackSmokeTests, VariadicsSemanticModelSpreadMemberSmokeTests)
// do not assert:
//   * value pack single-slot arity (7.1) -- the single (TArgs...) slot is flattened and its
//     tuple element IS the class's declared pack type parameter (an Assert.Same identity the
//     10.1 smoke checked only by name/IsPack); arity counts the pack as one slot;
//   * a plain (TArgs...) parameter (7.7) is NOT flattened and shares the SAME declared pack
//     element with BOTH the value pack parameter and the spread aggregate -- so `params` is
//     the sole marker of the Flattened_Call_Convention (TupleType is not interned in the
//     legacy model, so identity rides on the shared TypeParameterType element, not the
//     tuple instance);
//   * an empty spread stem driving MIST_VAR_0008 (10.4, 10.5) via the defensive guard;
//   * MIST_VAR_0007 stem collision (10.4) for BOTH a direct stem/member clash (a non-spread
//     field named exactly the stem) and a projection-name clash (a field named Stem1);
//   * the length-1 projection case (10.7): the family materializes exactly one projection
//     Item1 (the numeric suffix is always present) with the single bound element type, over
//     the aggregate Item : (TArgs...).
//
// MIST_VAR_0008 reachability. The grammar mandates an identifier for a field/property
// (Source/Mist/Grammar/Mist.storm), and the generated Storm parser halts on the first
// missing token, so a nameless spread member is a parse error, never a well-formed spread
// carrying an empty stem. The string.IsNullOrEmpty(stem) guard in
// SemanticModelBuilder.ResolveSpreadGeneratedMember is thus defensive and unreachable
// through normal parsing; this test exercises it faithfully by driving that entry point
// directly with an empty stem, using a genuine parsed spread/field AST node for the
// diagnostic location -- exactly as the task-10.4 smoke and the ProgramModel-track 5.9 test.
//
// Every test builds (or drives) the legacy SemanticModelBuilder entirely in-process. There
// are ZERO external process spawns: no dotnet run, no compiled DLL, no stdlib.

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
/// Task 10.9 consolidated edge-case unit tests over the classic
/// <see cref="SemanticModelBuilder"/>: value pack single-slot arity and the shared-pack
/// identity of the value pack, plain tuple parameter, and spread aggregate
/// (Requirements 7.1, 7.7); the empty-stem <c>MIST_VAR_0008</c> guard (Requirements 10.4,
/// 10.5); the <c>MIST_VAR_0007</c> stem/projection collisions (Requirement 10.4); and the
/// length-1 projection rule over the aggregate <c>Item : (TArgs...)</c> (Requirement 10.7).
/// </summary>
public sealed class VariadicsSemanticModelValueSideAndSpreadEdgeCaseTests
{
    private const string SpreadStemCollisionId = "MIST_VAR_0007";
    private const string EmptySpreadStemId = "MIST_VAR_0008";

    // A single variadic class carrying the pack `TArgs` and every value-side surface over
    // that one pack: the spread aggregate `Item`, a value pack parameter, a plain tuple
    // parameter, and a mixed value-parameter list. All four surfaces reference the SAME
    // symbolic pack, which is what the identity assertions below pin down.
    private const string ValueSideSource =
        "public class Bundle<params TArgs>\n"
        + "{\n"
        + "    public TArgs... Item;\n"
        + "\n"
        + "    public void TakePack(params TArgs args) { }\n"
        + "\n"
        + "    public void TakeTuple((TArgs...) whole) { }\n"
        + "\n"
        + "    public void Mixed(int number, params TArgs args, bool enabled) { }\n"
        + "}";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.1 - value pack single-slot arity: the (TArgs...) slot is flattened and
    // its single tuple element IS the class's declared pack; the pack counts as one slot.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValuePack_IsSingleFlattenedSlot_WhoseTupleElementIsTheDeclaredPack()
    {
        var (model, _) = BuildModel(ValueSideSource);
        var bundle = FindType<ClassType>(model, "Bundle");
        var pack = Assert.IsType<TypeParameterType>(bundle.TypeParameters[0]);
        Assert.True(pack.IsPack);

        // Requirement 7.1: `params TArgs args` is exactly ONE parameter slot, flattened, of
        // the single-slot tuple type (TArgs...).
        var args = Assert.Single(ParametersOf(model, "TakePack"));
        Assert.Equal("args", args.Name);
        Assert.True(args.HasFlattenedCallConvention);
        var tuple = Assert.IsType<TupleType>(args.DeclaredType);

        // Edge detail beyond the 10.1 smoke (which checked the element only by name/IsPack):
        // the value pack's tuple element IS the class's declared pack type parameter -- the
        // value side of the same symbolic pack, not some other TArgs-named parameter.
        var element = Assert.Single(tuple.ElementTypes);
        Assert.Same(pack, element);

        // Requirement 7.2 (the "arity counts the pack as one slot" facet of 7.1): the value
        // pack occupies a single slot, so `Mixed(int, params TArgs, bool)` has arity 3.
        var mixed = ParametersOf(model, "Mixed");
        Assert.Equal(3, mixed.Count);
        Assert.False(mixed[0].HasFlattenedCallConvention);
        Assert.Equal("args", mixed[1].Name);
        Assert.True(mixed[1].HasFlattenedCallConvention);
        _ = Assert.IsType<TupleType>(mixed[1].DeclaredType);
        Assert.False(mixed[2].HasFlattenedCallConvention);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.7 - a plain (TArgs...) parameter is NOT flattened and shares the SAME
    // declared pack element with the value pack parameter AND the spread aggregate. `params`
    // is the sole marker of the Flattened_Call_Convention.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlainTupleParameter_IsNotFlattened_AndAllSurfacesShareTheDeclaredPack()
    {
        var (model, _) = BuildModel(ValueSideSource);
        var bundle = FindType<ClassType>(model, "Bundle");
        var pack = Assert.IsType<TypeParameterType>(bundle.TypeParameters[0]);

        // Requirement 7.7: the plain `(TArgs...) whole` parameter (no `params`) receives one
        // tuple at the call site and is NOT flattened.
        var whole = Assert.Single(ParametersOf(model, "TakeTuple"));
        Assert.Equal("whole", whole.Name);
        Assert.False(whole.HasFlattenedCallConvention);
        var plainTuple = Assert.IsType<TupleType>(whole.DeclaredType);

        // The flattened value pack parameter.
        var args = Assert.Single(ParametersOf(model, "TakePack"));
        Assert.True(args.HasFlattenedCallConvention);
        var packTuple = Assert.IsType<TupleType>(args.DeclaredType);

        // The spread-generated aggregate `Item : (TArgs...)`.
        var family = Assert.Single(bundle.SpreadMembers);
        Assert.Equal("Item", family.Stem);

        // TupleType is not interned in the legacy model, so the three tuple INSTANCES differ;
        // identity rides on the shared symbolic element. The value pack, the plain tuple
        // parameter, and the spread aggregate all carry the SAME declared pack type parameter
        // as their single element -- only the `params` marker distinguishes the flattened
        // call form.
        Assert.Same(pack, Assert.Single(packTuple.ElementTypes));
        Assert.Same(pack, Assert.Single(plainTuple.ElementTypes));
        Assert.Same(pack, Assert.Single(family.AggregateType.ElementTypes));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.4 / 10.5 - an empty spread stem emits MIST_VAR_0008.
    //
    // Driven directly against the defensive guard (see the reachability note at the top of
    // the file), using genuine parsed spread/field AST nodes for the diagnostic location.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyStem_DrivesGuard_EmitsMistVar0008()
    {
        const string source =
            "public class Host<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}";

        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(VariadicsCompilerFixture.ParsedCleanly(parse));

        var fieldDecl = FindFirst<FieldDeclarationNode>(parse.Root!);
        Assert.NotNull(fieldDecl);
        Assert.True(
            SemanticModelBuilder.TryGetMemberSpread(fieldDecl!.Type, out var spread),
            "The parsed field's type should be a lone `TArgs...` member spread.");

        var diagnostics = new DiagnosticBag();
        var dummyMember = new Symbol("Item", SymbolKind.Field, SymbolOrigin.UserCode, new Scope(null), null, null);

        var result = SemanticModelBuilder.ResolveSpreadGeneratedMember(
            spread,
            stem: string.Empty,
            aggregateMember: dummyMember,
            SpreadMemberKind.Field,
            enclosingType: null,
            fieldDecl!,
            new Scope(null),
            new SourceText("test.mist", source),
            diagnostics);

        // The malformed family is rejected with an error type and no family is recorded.
        Assert.Same(ErrorType.Instance, result);

        var emitted = diagnostics.ToImmutableArray();
        Assert.Equal(1, CountDiagnostics(emitted, EmptySpreadStemId));
        var diagnostic = emitted.Single(d => d.Id.Value == EmptySpreadStemId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("non-empty stem", diagnostic.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.4 - MIST_VAR_0007 stem collision: a direct stem/member clash (a
    // non-spread field named exactly the aggregate stem `Item`) and a projection-name clash
    // (a field named `Item1`), mirroring the 10.4 collision tests.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonSpreadFieldNamedExactlyTheStem_EmitsMistVar0007()
    {
        // Direct clash: a regular field named exactly the stem `Item` (the aggregate name),
        // invading the reserved spread namespace.
        var (_, diagnostics) = BuildModel(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Item;\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
        var diagnostic = diagnostics.Single(d => d.Id.Value == SpreadStemCollisionId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Item", diagnostic.Message);
    }

    [Fact]
    public void ProjectionNameFieldCollision_EmitsMistVar0007()
    {
        // Projection-name clash: a field named `Item1` collides with the spread's reserved
        // 1-based projection name.
        var (_, diagnostics) = BuildModel(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Item1;\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.7 - the length-1 projection case: over the aggregate Item : (TArgs...),
    // a bound pack of length 1 yields exactly one projection Item1 (the numeric suffix is
    // always present) with the single element type.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LengthOnePack_YieldsSingleItem1Projection_OverAggregateTuple()
    {
        var (model, diagnostics) = BuildModel(
            "public class Unit<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, SpreadStemCollisionId));
        Assert.Equal(0, CountDiagnostics(diagnostics, EmptySpreadStemId));

        var unit = FindType<ClassType>(model, "Unit");
        var pack = Assert.IsType<TypeParameterType>(unit.TypeParameters[0]);

        // The family records the aggregate Item : (TArgs...); while symbolic the aggregate
        // carries the pack as its single element (projections follow once the pack binds).
        var family = Assert.Single(unit.SpreadMembers);
        Assert.Equal("Item", family.Stem);
        Assert.Equal("Item", family.AggregateMember.Name);
        Assert.Same(pack, Assert.Single(family.AggregateType.ElementTypes));

        // Length-1 binding (Requirement 10.7): exactly one projection, Item1, with the single
        // bound element type; the numeric suffix is present even at length 1.
        var single = Assert.Single(family.MaterializeProjections(new IType[] { LegacyPrimitiveTypes.Int }));
        Assert.Equal("Item1", single.Name);
        Assert.Same(LegacyPrimitiveTypes.Int, single.Type);
        Assert.Equal("Item1", family.ProjectionName(1));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model together with every construction-stage diagnostic. Parameter type resolution,
    /// the value-side representation, and the spread-member rules all run during model
    /// construction, so inspecting the built types, parameter <see cref="Symbol"/> instances,
    /// and construction diagnostics is sufficient.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            "Edge-case source is expected to parse without lex/parse errors.");

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

        Assert.NotNull(model);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Returns the declared parameter <see cref="Symbol"/> instances (in declaration order)
    /// of the single source method named <paramref name="methodName"/>, read from the
    /// declaring AST node so the assertions inspect exactly what the canonical
    /// <c>RegisterParameters</c> pass produced.
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
    /// Finds the single source type named <paramref name="name"/> whose declared type is a
    /// <typeparamref name="T"/> (for example <see cref="ClassType"/>).
    /// </summary>
    private static T FindType<T>(ISemanticModel model, string name)
        where T : class, IType
    {
        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == name && s.DeclaredType is T);
        Assert.NotNull(symbol);
        return (T)symbol!.DeclaredType!;
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }

    /// <summary>
    /// Depth-first search for the first descendant (or the node itself) of type
    /// <typeparamref name="T"/>, walking the Storm AST via <c>Children()</c>.
    /// </summary>
    private static T? FindFirst<T>(Storm.Runtime.SyntaxNode node)
        where T : Storm.Runtime.SyntaxNode
    {
        if(node is T match)
        {
            return match;
        }

        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            var found = FindFirst<T>(child);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }
}