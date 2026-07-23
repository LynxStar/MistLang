// Feature: variadics - Smoke/edge tests for the classic (legacy Mist.Semantics)
// spread-generated member representation (Task 10.4). Requirements: 10.1, 10.2, 10.3,
// 10.4, 10.5, 10.6, 10.7, 10.8.
//
// Task 10.4 adds spread-generated member families to the LEGACY Mist.Semantics model (the
// classic, emitting pipeline), mirroring the completed ProgramModel track (task 5.4). A
// member-declaring spread of a type-parameter pack -- `public TArgs... Item;` (field) or
// `public TArgs... Slot { get; }` (property) -- surfaces:
//   * an aggregate member named by the stem, of the value-pack tuple type (TArgs...); plus
//   * the 1-based element projections Stem1..StemN, materialized from the aggregate tuple's
//     bound elements once the pack binds a concrete Type[] (deferred to backend
//     monomorphization). While the pack is symbolic the aggregate carries the pack as its
//     single element, so empty/length-1 packs fall out of that deferral (10.6/10.7).
// The family is recorded on the enclosing ClassType/StructType/AbstractType.SpreadMembers.
//
// This drives SemanticModelBuilder entirely in-process (ZERO external process spawns: no
// dotnet run, no compiled DLL, no stdlib) and asserts, over the classic model:
//   - field spread -> aggregate field Item : (TArgs...) + Field family (10.1);
//   - property spread -> aggregate property Slot : (TArgs...) + Property family (10.1);
//   - a struct field spread records a family (10.1, proof-code ShapePair shape);
//   - the projection rule: N projections named Stem1..StemN with the bound element types,
//     empty -> zero (10.6), length-1 -> Stem1 (10.7);
//   - two spreads of the same pack correlate at the same N via a shared pack (10.8);
//   - MIST_VAR_0007 on a stem/projection-name collision, a method named the stem, and two
//     stems that prefix one another (10.2/10.3);
//   - MIST_VAR_0008 on an empty stem (10.4/10.5) -- a defensive guard driven directly,
//     since a nameless spread member cannot be produced by the halt-on-first-error parser.

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
/// Smoke and edge-case tests asserting that the classic <see cref="SemanticModelBuilder"/>
/// expands a member-declaring pack spread into an aggregate member of the value-pack tuple
/// type <c>(TArgs...)</c> plus a recorded <see cref="SemanticSpreadMember"/> family (whose
/// bound length yields the 1-based projections <c>Stem1 .. StemN</c>), reserves the stem
/// namespace (<c>MIST_VAR_0007</c>), and rejects an empty stem (<c>MIST_VAR_0008</c>).
/// Requirements 10.1-10.8.
/// </summary>
public sealed class VariadicsSemanticModelSpreadMemberSmokeTests
{
    private const string SpreadStemCollisionId = "MIST_VAR_0007";
    private const string EmptySpreadStemId = "MIST_VAR_0008";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1 - field spread: aggregate `Item : (TArgs...)` + Field family.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldSpread_ProducesAggregateFieldAndRecordsFamily()
    {
        var (model, diagnostics) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");

        AssertNoSpreadDiagnostics(diagnostics);

        var bundle = FindType<ClassType>(model, "Bundle");
        var pack = Assert.IsType<TypeParameterType>(bundle.TypeParameters[0]);
        Assert.True(pack.IsPack);

        // The family is recorded on the enclosing type: stem, pack, aggregate, Field kind.
        var family = Assert.Single(bundle.SpreadMembers);
        Assert.Equal("Item", family.Stem);
        Assert.Equal(SpreadMemberKind.Field, family.Kind);
        Assert.Same(pack, family.Pack);

        // The aggregate is the whole value-pack tuple (TArgs...): while symbolic it carries
        // the declared pack as its single element (the projections follow once it binds).
        var element = Assert.Single(family.AggregateType.ElementTypes);
        Assert.Same(pack, element);

        // The aggregate member is a REAL public field named by the stem, of the aggregate
        // tuple type, discoverable in the type's member scope (10.1).
        var aggregate = family.AggregateMember;
        Assert.Equal("Item", aggregate.Name);
        Assert.Equal(SymbolKind.Field, aggregate.Kind);
        Assert.Equal(Accessibility.Public, aggregate.Accessibility);
        Assert.Same(family.AggregateType, aggregate.DeclaredType);
        Assert.Same(aggregate, bundle.MemberScope.Lookup("Item"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1 - property spread: aggregate `Slot : (TArgs...)` + Property family.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertySpread_ProducesAggregatePropertyAndRecordsFamily()
    {
        var (model, diagnostics) = BuildModel(
            "public class Row<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Slot { get; }\n"
            + "}");

        AssertNoSpreadDiagnostics(diagnostics);

        var row = FindType<ClassType>(model, "Row");
        var pack = Assert.IsType<TypeParameterType>(row.TypeParameters[0]);

        var family = Assert.Single(row.SpreadMembers);
        Assert.Equal("Slot", family.Stem);
        Assert.Equal(SpreadMemberKind.Property, family.Kind);
        Assert.Same(pack, family.Pack);

        // The aggregate member is a REAL property named by the stem, of the tuple type.
        var aggregate = family.AggregateMember;
        Assert.Equal("Slot", aggregate.Name);
        Assert.Equal(SymbolKind.Property, aggregate.Kind);
        Assert.Equal(Accessibility.Public, aggregate.Accessibility);
        Assert.Same(family.AggregateType, aggregate.DeclaredType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1 - a variadic STRUCT field spread records a family too (proof-code
    // ShapePair shape: a value type carrying `public TShapes... Shape;`).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StructFieldSpread_RecordsFamily()
    {
        var (model, diagnostics) = BuildModel(
            "public struct ShapePair<Type[2]:TShapes>\n"
            + "{\n"
            + "    public TShapes... Shape;\n"
            + "}");

        AssertNoSpreadDiagnostics(diagnostics);

        var shapePair = FindType<StructType>(model, "ShapePair");
        var pack = Assert.IsType<TypeParameterType>(shapePair.TypeParameters[0]);
        Assert.True(pack.IsPack);

        var family = Assert.Single(shapePair.SpreadMembers);
        Assert.Equal("Shape", family.Stem);
        Assert.Equal(SpreadMemberKind.Field, family.Kind);
        Assert.Same(pack, family.Pack);
        Assert.Same(pack, Assert.Single(family.AggregateType.ElementTypes));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1 / 10.6 / 10.7 - the projection rule: for a bound pack of length N the
    // family yields exactly N projections Stem1..StemN with the bound element types in
    // order; an empty binding yields zero (10.6); a length-1 binding yields Stem1 (10.7).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MaterializeProjections_YieldsStemPrefixedProjectionsWithElementTypes()
    {
        var (model, _) = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");
        var family = Assert.Single(FindType<ClassType>(model, "Bundle").SpreadMembers);

        var intType = ElementType("Int32");
        var stringType = ElementType("String");
        var boolType = ElementType("Boolean");

        // Length-N (N = 3): exactly N projections, 1-based Item1..Item3, element types in
        // order.
        var projections = family.MaterializeProjections(new IType[] { intType, stringType, boolType });
        Assert.Equal(3, projections.Count);
        Assert.Equal("Item1", projections[0].Name);
        Assert.Same(intType, projections[0].Type);
        Assert.Equal("Item2", projections[1].Name);
        Assert.Same(stringType, projections[1].Type);
        Assert.Equal("Item3", projections[2].Name);
        Assert.Same(boolType, projections[2].Type);

        // Length-1 (10.7): the single projection Item1; the numeric suffix is always present.
        var single = Assert.Single(family.MaterializeProjections(new IType[] { intType }));
        Assert.Equal("Item1", single.Name);
        Assert.Same(intType, single.Type);

        // Empty pack (10.6): zero projections.
        Assert.Empty(family.MaterializeProjections(Array.Empty<IType>()));

        // The projection-name rule is stem + 1-based index.
        Assert.Equal("Item1", family.ProjectionName(1));
        Assert.Equal("Item2", family.ProjectionName(2));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.8 - two spreads of the SAME pack correlate at the same N: two families
    // over one shared pack, so binding produces index-aligned projections.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CorrelatedSpreads_ShareOnePackAndAlignAtN()
    {
        var (model, diagnostics) = BuildModel(
            "public class Pair<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Key;\n"
            + "    public TArgs... Value;\n"
            + "}");

        AssertNoSpreadDiagnostics(diagnostics);

        var pair = FindType<ClassType>(model, "Pair");
        var pack = Assert.IsType<TypeParameterType>(pair.TypeParameters[0]);

        // Two distinct families, both over the SAME pack instance -> they bind identically.
        Assert.Equal(2, pair.SpreadMembers.Count);
        var key = pair.SpreadMembers.Single(s => s.Stem == "Key");
        var value = pair.SpreadMembers.Single(s => s.Stem == "Value");
        Assert.Same(pack, key.Pack);
        Assert.Same(pack, value.Pack);

        // Binding to the same Type[] of length N yields index-aligned projections: Key1..KeyN
        // and Value1..ValueN over the same element type at each index.
        var elements = new IType[] { ElementType("Int32"), ElementType("String") };
        var keyProjections = key.MaterializeProjections(elements);
        var valueProjections = value.MaterializeProjections(elements);

        Assert.Equal(2, keyProjections.Count);
        Assert.Equal(keyProjections.Count, valueProjections.Count);
        for(var k = 0; k < keyProjections.Count; k++)
        {
            Assert.Equal($"Key{k + 1}", keyProjections[k].Name);
            Assert.Equal($"Value{k + 1}", valueProjections[k].Name);
            Assert.Same(elements[k], keyProjections[k].Type);
            Assert.Same(elements[k], valueProjections[k].Type);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.2 / 10.3 - MIST_VAR_0007: a member outside the family using the stem or
    // an S{i} projection name, a method named the stem, and two stems prefixing one another.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectionNameFieldCollision_EmitsMistVar0007()
    {
        var (_, diagnostics) = BuildModel(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Item1;\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
        var diagnostic = diagnostics.Single(d => d.Id.Value == SpreadStemCollisionId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Item", diagnostic.Message);
    }

    [Fact]
    public void MethodNamedExactlyTheStem_EmitsMistVar0007()
    {
        var (_, diagnostics) = BuildModel(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public void Item() { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
    }

    [Fact]
    public void TwoStemsPrefixingOneAnother_EmitMistVar0007Once()
    {
        var (_, diagnostics) = BuildModel(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public TArgs... Item1;\n"
            + "}");

        // Reported once, on the later stem, since `Item` prefixes `Item1`.
        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
    }

    [Fact]
    public void NonCollidingMembers_DoNotEmitMistVar0007()
    {
        var (model, diagnostics) = BuildModel(
            "public class Fine<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Count;\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, SpreadStemCollisionId));

        // The spread family is still recorded and the non-colliding field is untouched.
        var fine = FindType<ClassType>(model, "Fine");
        Assert.Equal("Item", Assert.Single(fine.SpreadMembers).Stem);
        Assert.NotNull(fine.MemberScope.Lookup("Count"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.4 / 10.5 - MIST_VAR_0008: an empty spread stem.
    //
    // The guard is defensive: a nameless spread member cannot be produced by the
    // halt-on-first-error parser (the grammar mandates an identifier), so this drives the
    // model-construction entry point ResolveSpreadGeneratedMember directly with an empty
    // stem, using a genuine parsed spread/field AST node for the diagnostic location.
    // Mirrors the ProgramModel track's edge-case test.
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
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning the
    /// model and every construction diagnostic (where MIST_VAR_0007 is reported).
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            "Smoke-test source is expected to parse without lex/parse errors.");

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
    /// Finds the single source type named <paramref name="name"/> whose declared type is a
    /// <typeparamref name="T"/> (for example <see cref="ClassType"/> or <see cref="StructType"/>).
    /// </summary>
    private static T FindType<T>(ISemanticModel model, string name)
        where T : class, IType
    {
        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == name && s.DeclaredType is T);
        Assert.NotNull(symbol);
        return (T)symbol!.DeclaredType!;
    }

    /// <summary>Builds a simple element type for materializing projections.</summary>
    private static ClassType ElementType(string name)
    {
        return new ClassType(name, new Scope(null), "Standard");
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

    private static void AssertNoSpreadDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadStemCollisionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == EmptySpreadStemId);
    }
}