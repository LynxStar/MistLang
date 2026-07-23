// Feature: variadics - Unit tests for value-side representation and spread-member edge cases (Task 5.9)
// Requirements: 7.1, 7.7, 10.4, 10.5, 10.7
//
// Dedicated edge-case unit tests that complement (without duplicating) the Task 5.1
// value-pack smoke tests (VariadicsValuePackSmokeTests) and the Task 5.4 spread-member
// smoke tests (VariadicsSpreadMemberSmokeTests). The focus is the specific edges Task 5.9
// calls out:
//   * value pack single-slot arity (7.1) -- asserting the (TArgs...) tuple actually carries
//     the declared pack as its single symbolic element, an identity detail the 5.1 smoke
//     did not check;
//   * a plain (TArgs...) parameter (7.7) sharing ONE interned tuple with the value pack AND
//     the spread aggregate, so `params` is the sole marker of the flattened call convention;
//   * an empty spread stem driving MIST_VAR_0008 (10.4, 10.5) -- see the reachability note;
//   * focused MIST_VAR_0007 stem-collision cases the 5.4 smoke did NOT cover (a method named
//     the stem; a non-spread member named exactly the stem);
//   * the length-1 projection case (10.7): the length-1 bound aggregate (T) has exactly one
//     positional field Item1 -- the single projection S1, whose numeric suffix is always
//     present.
//
// MIST_VAR_0008 reachability. The grammar mandates `name:IDENTIFIER` for a field/property
// (Source/Mist/Grammar/Mist.storm), and the generated Storm parser halts on the first
// missing token: `Require(kind)` sets `_hasError`, reports a parse diagnostic, and returns
// the current -- wrong -- token; it never synthesizes an empty-lexeme identifier. A nameless
// spread field is therefore a parse error, not a well-formed spread member carrying an empty
// stem. The `string.IsNullOrEmpty(stem)` guard in
// SpreadMemberBuilder.ResolveAggregateAndRecord is thus defensive and is not reachable
// through normal parsing. This test exercises that guard faithfully by driving the
// program-model entry point (ResolveAggregateAndRecord) directly with an empty stem, using
// real parsed spread/field AST nodes so the reported location is genuine.
//
// Every test builds the program model in-process via ProgramModelBuilder.Build (or drives a
// program-model builder component directly). There are ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using FieldDeclarationNode = Mist.Generated.FieldDeclarationNode;
using ProgramModelImpl = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 5.9 edge-case unit tests for the value-side pack representation (Requirements 7.1,
/// 7.7) and the spread-generated-member edges (Requirements 10.4, 10.5, 10.7). Each test
/// builds the program model in-process and inspects the resulting parameters, spread-member
/// families, and diagnostics, or drives a builder component directly for the defensive
/// empty-stem guard.
/// </summary>
public sealed class VariadicsValueSideAndSpreadMemberEdgeCaseTests : IDisposable
{
    private const string EmptySpreadStemId = "MIST_VAR_0008";
    private const string SpreadStemCollisionId = "MIST_VAR_0007";

    private readonly string outputDir;

    public VariadicsValueSideAndSpreadMemberEdgeCaseTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsEdge_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the temporary build directory.
            }
        }

        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.1 - value pack single-slot arity: the (TArgs...) tuple slot
    // carries the declared pack as its single symbolic element.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValuePack_IsSingleSlot_WhoseTupleCarriesTheDeclaredPack()
    {
        var (model, _) = Build(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public Bundle(params TArgs values) { }\n"
            + "}");
        var bundle = FindConcreteType(model, "Bundle");
        var pack = Assert.Single(bundle.TypeParametersList);

        var ctor = Assert.Single(bundle.ConstructorsList);

        // Requirement 7.1: `params TArgs values` is exactly ONE parameter slot marked with
        // the Flattened_Call_Convention.
        var values = Assert.Single(ctor.ParametersList);
        Assert.Equal("values", values.Name);
        Assert.True(values.HasFlattenedCallConvention);

        // Edge detail (beyond the 5.1 smoke, which only checked "is a tuple"): the value
        // pack's type is the tuple (TArgs...) whose single element IS the class's declared
        // pack type parameter -- the value side of the same symbolic pack, not some other
        // tuple.
        var tuple = Assert.IsAssignableFrom<ITupleType>(values.Type);
        var element = Assert.Single(tuple.ElementTypes);
        Assert.Same(pack, element);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.7 - a plain (TArgs...) parameter (no `params`) is an ordinary,
    // NOT-flattened tuple parameter, and it shares ONE interned (TArgs...) tuple with
    // both the value pack parameter and the spread-generated aggregate. `params` is the
    // sole marker of the Flattened_Call_Convention.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PlainTupleParameter_SharesOneInternedTupleWithValuePackAndAggregate()
    {
        var (model, _) = Build(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "\n"
            + "    public Bundle(params TArgs values) { }\n"
            + "\n"
            + "    public void Take((TArgs...) whole) { }\n"
            + "}");
        var bundle = FindConcreteType(model, "Bundle");

        // The value pack parameter: flattened.
        var ctor = Assert.Single(bundle.ConstructorsList);
        var values = Assert.Single(ctor.ParametersList);
        Assert.True(values.HasFlattenedCallConvention);

        // Requirement 7.7: the plain `(TArgs...) whole` parameter receives one tuple at the
        // call site and is NOT flattened -- the `params` modifier is the sole marker.
        var take = bundle.MethodsList.Single(m => m.Name == "Take");
        var whole = Assert.Single(take.ParametersList);
        Assert.False(whole.HasFlattenedCallConvention);

        // The spread-generated aggregate `Item` is a real field of the aggregate tuple type.
        var aggregate = Assert.Single(bundle.FieldsList);
        Assert.Equal("Item", aggregate.Name);

        // All three surfaces resolve to the SAME interned (TArgs...) tuple: the flattened
        // value pack, the ordinary single-tuple parameter, and the spread aggregate. Only
        // the `params` marker distinguishes the flattened-call form -- the type is one and
        // the same.
        _ = Assert.IsAssignableFrom<ITupleType>(whole.Type);
        Assert.Same(values.Type, whole.Type);
        Assert.Same(values.Type, aggregate.Type);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.4 / 10.5 - an empty spread stem emits MIST_VAR_0008.
    //
    // The guard is defensive (see the reachability note at the top of the file): a nameless
    // spread field cannot be produced by the halt-on-first-error parser, so this drives the
    // program-model entry point ResolveAggregateAndRecord directly with an empty stem, using
    // genuine parsed spread/field AST nodes for the diagnostic location.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyStem_DrivesProgramModelGuard_EmitsMistVar0008()
    {
        // Parse a valid spread field to obtain real AST nodes: the spread operand node and
        // the field declaration node that supplies the diagnostic location.
        var root = ParseCleanly(
            "public class Host<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");
        var fieldDecl = FindFirst<FieldDeclarationNode>(root);
        Assert.NotNull(fieldDecl);
        Assert.True(
            SpreadMemberBuilder.TryGetMemberSpread(fieldDecl!.Type, out var spread),
            "The parsed field's type should be a lone `TArgs...` member spread.");

        // Construct a minimal program-model builder context in-process (no pipeline, no
        // process spawn). The empty-stem guard fires before the pack operand or host scope
        // is touched, so a bare context and host suffice to exercise it.
        var diagnostics = new DiagnosticBag();
        var context = new BuilderContext(
            new Scope(null),
            new ProgramModelImpl(),
            diagnostics,
            NullAmbientTypeProvider.Instance);
        var typeResolver = new ScopeResolver(context);
        var spreadBuilder = new SpreadMemberBuilder(context, typeResolver);
        var host = new ClassType("Host", "TestNs", new Scope(null), null, null);

        var result = spreadBuilder.ResolveAggregateAndRecord(
            spread,
            stem: string.Empty,
            host,
            SpreadMemberKind.Field,
            new MemberModifiers { Accessibility = Accessibility.Public },
            OwnershipKind.Unrestricted,
            fieldDecl!);

        // The malformed family is rejected: an error type is returned and NO family is
        // recorded on the host.
        Assert.Same(ErrorType.Instance, result);
        Assert.Empty(host.SpreadMembersList);

        // Exactly one MIST_VAR_0008, reported as an error with the non-empty-stem message.
        var emitted = diagnostics.ToImmutableArray();
        Assert.Equal(1, CountDiagnostics(emitted, EmptySpreadStemId));
        var diagnostic = emitted.Single(d => d.Id.Value == EmptySpreadStemId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("non-empty stem", diagnostic.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.2 / 10.3 - focused MIST_VAR_0007 stem-collision cases not covered by
    // the 5.4 smoke test (which covered a projection-name field `Item1` and two stems
    // prefixing one another). Here: a method named exactly the stem, and a non-spread field
    // named exactly the stem (the aggregate name `S` itself, not an `S{i}` projection).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodNamedExactlyTheStem_EmitsMistVar0007()
    {
        var (_, diagnostics) = Build(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public void Item() { }\n"
            + "}");

        // The spread owns the aggregate name `Item`; a method named `Item` invades that
        // reserved namespace.
        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
        var diagnostic = diagnostics.Single(d => d.Id.Value == SpreadStemCollisionId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Item", diagnostic.Message);
    }

    [Fact]
    public void NonSpreadMemberNamedExactlyTheStem_EmitsMistVar0007()
    {
        // A regular field named exactly the stem `Item` (the aggregate name), distinct from
        // the 5.4 smoke's projection-name (`Item1`) case.
        var (_, diagnostics) = Build(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Item;\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.7 - the length-1 projection case: a pack bound to length 1 yields the
    // aggregate S of type (T) and the single projection S1 (the numeric suffix is always
    // present).
    //
    // Deferral note. While the pack is symbolic the recorded family carries the aggregate
    // (TArgs...) as a single pack element; the N projections S1..SN materialize only when
    // the pack binds a concrete Type[] (deferred to backend monomorphization, task 14.1 --
    // nothing in the program model consumes SpreadMember.AggregateType yet). This test
    // asserts what IS available in-process: the family is recorded structurally, and the
    // bound length-1 aggregate (T) -- what (TArgs...) re-interns to when TArgs binds [T] --
    // exposes exactly one positional field Item1, the single projection with its always-
    // present numeric suffix.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LengthOnePack_BoundAggregate_HasExactlyOneItem1Projection()
    {
        var (model, _) = Build(
            "public class Unit<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");
        var unit = FindConcreteType(model, "Unit");

        // The family is recorded with a symbolic aggregate that carries the pack as its
        // single element; projections are deferred until the pack binds.
        var family = Assert.Single(unit.SpreadMembersList);
        Assert.Equal("Item", family.Stem);
        _ = Assert.Single(family.AggregateType.ElementTypes);

        // Drive the binding/tuple path: when TArgs binds the length-1 Type[] [T], the
        // aggregate (TArgs...) re-interns to the tuple (T). Build that bound aggregate via
        // the interner (the same GetOrCreateTupleType the resolver uses) and assert it
        // exposes exactly one positional projection named Item1 of element type T.
        var interner = new TypeInterner();
        var elementType = new ClassType("T", "TestNs", new Scope(null), null, null);
        var boundAggregate = interner.GetOrCreateTupleType(new IMistType[] { elementType });

        // The aggregate is a length-1 tuple (T), not a bare scalar.
        var element = Assert.Single(boundAggregate.ElementTypes);
        Assert.Same(elementType, element);

        // Exactly one projection, and its numeric suffix is present even at length 1.
        var projection = Assert.Single(boundAggregate.PositionalFields);
        Assert.Equal("Item1", projection.Name);
        Assert.Same(elementType, projection.Type);
        Assert.Equal("Item1", TupleType.PositionalName(1));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the program model entirely in-process,
    /// returning the model together with every diagnostic collected during construction
    /// (where MIST_VAR_0007 is reported). Asserts the source parses without error.
    /// </summary>
    private (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) Build(string source)
    {
        var root = ParseCleanly(source);

        var unit = new CompilationUnit(new SourceText("test.mist", source));
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(root),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Lexes and parses <paramref name="source"/> in-process and returns the AST root,
    /// asserting that no lex/parse error was produced.
    /// </summary>
    private static Storm.Runtime.SyntaxNode ParseCleanly(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        return parseResult.Root!;
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

    private static ConcreteType FindConcreteType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<ConcreteType>()
            .Where(t => t.Origin == TypeOrigin.Source && SimpleName(t.Name) == simpleName)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one source type named '{simpleName}', found {matches.Count}.");
        return matches[0];
    }

    private static string SimpleName(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }
}