// Feature: variadics, task 19.5: property test for empty-pack coherence (classic pipeline).
//
// Property 13: Empty-pack coherence.
// **Validates: Requirements 4.3, 6.8, 10.6, 11.4, 15.4**
//
// For any family and method over an empty pack, the empty pack yields an arity-0
// construction, zero spread slots, zero generated members, the zero-argument callable
// (Func<TResult>), and vacuously satisfies any whole-pack constraint (no MIST_VAR_0009).
//
// This test exercises the empty pack in different positions through the classic pipeline.
// The property generates random result types and verifies each axis of coherence:
//   * Binding rule accepts an empty pack (Req 4.3) via VariadicsCompilerFixture.Compile
//   * Spread of an empty pack produces zero slots (Req 6.8) via BindPackSlots
//   * Spread-generated members: empty pack yields zero projections (Req 10.6) via BuildModel
//   * Whole-pack constraint vacuously satisfied (Req 11.4) via AnalyzeClassic
//   * The zero-argument callable Func<TResult> (Req 15.4) via VariadicsCompilerFixture.Compile
//
// All tests run entirely in-process with ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Coherence tests for <b>Property 13 (Empty-pack coherence)</b> over the classic pipeline.
/// Each empty-pack axis is verified deterministically: the two axes whose behavior depends on
/// the result type (the zero-arg callable <c>func&lt;TResult&gt;</c> and the empty middle pack)
/// are <see cref="TheoryAttribute"/> cases over int/string/bool, while the type-independent
/// axes (zero spread slots, zero generated members, vacuous constraint) are single
/// <see cref="FactAttribute"/> cases. This replaces an earlier [Property] that dispatched on an
/// (axis x result-type) scenario and re-ran the same nine meaningful cases 150 times.
/// <list type="bullet">
///   <item>The binding rule accepts an empty pack (Req 4.3).</item>
///   <item>Spread of an empty pack produces zero slots (Req 6.8).</item>
///   <item>Zero generated members for an empty pack (Req 10.6).</item>
///   <item>The zero-argument callable Func&lt;TResult&gt; type-checks (Req 15.4).</item>
///   <item>Whole-pack constraint is vacuously satisfied (Req 11.4).</item>
/// </list>
/// <b>Validates: Requirements 4.3, 6.8, 10.6, 11.4, 15.4.</b>
/// </summary>
public sealed class VariadicsEmptyPackCoherencePropertyTests
{
    private const string ConstraintDiagId = "MIST_VAR_0009";

    // ═══════════════════════════════════════════════════════════════════════
    // Axis: Func<TResult> compiles (Req 4.3, 15.4)
    // ═══════════════════════════════════════════════════════════════════════

    private static bool VerifyFuncZeroArgCallable(string resultType)
    {
        var defaultExpr = DefaultLiteral(resultType);
        var source = $"public class Program\n"
            + $"{{\n"
            + $"    public static {resultType} Test()\n"
            + $"    {{\n"
            + $"        func<{resultType}> f = () => {defaultExpr};\n"
            + $"        return f();\n"
            + $"    }}\n"
            + $"}}\n";

        var result = VariadicsCompilerFixture.Compile(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"func<{resultType}> (empty pack zero-arg callable) produced {errors.Count} error(s):\n"
            + string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}")));

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Axis: Spread of empty pack -> zero slots (Req 6.8)
    // ═══════════════════════════════════════════════════════════════════════

    private static bool VerifySpreadZeroSlots()
    {
        // An empty operand spread through BindPackSlots yields zero elements (pure rule).
        var slots = new[] { TypeCheckerAnalyzer.PackBindingSlotKind.UnbracketedPack };
        var emptyGroup = TypeCheckerAnalyzer.PackUseSiteArgument.Group(
            Array.Empty<IType>());

        var bracketed = TypeCheckerAnalyzer.BindPackSlots(slots, new[] { emptyGroup });
        Assert.Equal(TypeCheckerAnalyzer.PackBindingStatus.Bound, bracketed.Status);
        var binding = Assert.Single(bracketed.Slots);
        Assert.True(binding.IsPack);
        Assert.Empty(binding.Elements);

        // Also verify with zero flat arguments (the flat spelling of the empty pack).
        var flat = TypeCheckerAnalyzer.BindPackSlots(slots,
            Array.Empty<TypeCheckerAnalyzer.PackUseSiteArgument>());
        Assert.Equal(TypeCheckerAnalyzer.PackBindingStatus.Bound, flat.Status);
        var flatBinding = Assert.Single(flat.Slots);
        Assert.True(flatBinding.IsPack);
        Assert.Empty(flatBinding.Elements);

        // Value-side: decomposing an empty tuple against zero slots is well-typed.
        Assert.Equal(
            TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped,
            TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
                Array.Empty<IType>(), Array.Empty<IType>(), model: null, out var index));
        Assert.Equal(-1, index);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Axis: Generated members = zero for empty pack (Req 10.6)
    // ═══════════════════════════════════════════════════════════════════════

    private static bool VerifyGeneratedMembersZero(string _)
    {
        // Use Parse + BuildModel (the approach existing spread-member tests use).
        var source = "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}\n";

        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Source did not parse cleanly: {source}");

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

        // Find the Bundle type via AllSymbols.
        var bundleSymbol = model.AllSymbols.FirstOrDefault(s => s.Name == "Bundle" && s.DeclaredType is ClassType);
        Assert.NotNull(bundleSymbol);
        var bundle = (ClassType)bundleSymbol!.DeclaredType!;
        var family = Assert.Single(bundle.SpreadMembers);
        Assert.Equal("Item", family.Stem);

        // The key assertion: MaterializeProjections with zero elements yields zero projections.
        var projections = family.MaterializeProjections(Array.Empty<IType>());
        Assert.Empty(projections);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Axis: Vacuous constraint satisfaction (Req 11.4)
    // ═══════════════════════════════════════════════════════════════════════

    private static bool VerifyVacuousConstraint()
    {
        // Drive CheckPackConstraintSatisfaction directly with zero elements (the pure rule).
        // An empty pack vacuously satisfies any constraint (Req 11.4) -- zero elements means
        // zero opportunities to violate, so no MIST_VAR_0009 should be emitted.
        var tp = new TypeParameterType("TArgs", 0)
        {
            IsPack = true,
            IsUnbracketedPack = true,
            Constraints = new TypeParameterConstraints { HasStructConstraint = true },
        };

        var dummyNode = GetDummyNode();
        var bag = new DiagnosticBag();

        // Empty array of elements: the pack is bound to [].
        TypeCheckerAnalyzer.CheckPackConstraintSatisfaction(
            Array.Empty<IType>(), tp, dummyNode, model: null!, bag);

        var violations = bag.ToImmutableArray().Where(d => d.Id.Value == ConstraintDiagId).ToList();
        Assert.True(violations.Count == 0,
            $"Empty pack with struct constraint produced {violations.Count} MIST_VAR_0009 "
            + "(expected zero for vacuous satisfaction):\n"
            + string.Join("\n", violations.Select(d => $"  {d.Id.Value}: {d.Message}")));

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Axis: Binding rule accepts empty middle pack (Req 4.3)
    // ═══════════════════════════════════════════════════════════════════════

    private static bool VerifyBindingRuleEmptyMiddle(string resultType)
    {
        // Between<TFirst, params TMid, TLast> with only 2 args binds TMid=[]
        var source = "public class Between<TFirst, params TMid, TLast>\n"
            + "{\n"
            + "}\n"
            + "\n"
            + "public class Program\n"
            + "{\n"
            + "    public static void Test()\n"
            + "    {\n"
            + $"        var b = new Between<{resultType}, bool>();\n"
            + "    }\n"
            + "}\n";

        var result = VariadicsCompilerFixture.Compile(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Between<{resultType}, bool> (empty middle pack) produced {errors.Count} error(s):\n"
            + string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}")));

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Targeted unit tests for each axis (deterministic coverage)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// func&lt;TResult&gt; compiles for each result type: empty pack = zero-arg callable
    /// (Req 4.3, 15.4).
    /// </summary>
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    public void EmptyPack_FuncZeroArgCallable_CompilesCleanly(string resultType)
    {
        Assert.True(VerifyFuncZeroArgCallable(resultType));
    }

    /// <summary>
    /// Spread of an empty pack produces zero slots (Req 6.8).
    /// </summary>
    [Fact]
    public void EmptyPack_SpreadProducesZeroSlots()
    {
        Assert.True(VerifySpreadZeroSlots());
    }

    /// <summary>
    /// A class with TArgs... spread generates zero members when TArgs=[] (Req 10.6).
    /// </summary>
    [Fact]
    public void EmptyPack_GeneratesZeroMembers()
    {
        Assert.True(VerifyGeneratedMembersZero("int"));
    }

    /// <summary>
    /// A whole-pack constraint is vacuously satisfied for TArgs=[] (Req 11.4).
    /// </summary>
    [Fact]
    public void EmptyPack_VacuousConstraintSatisfaction()
    {
        Assert.True(VerifyVacuousConstraint());
    }

    /// <summary>
    /// The binding rule accepts an empty pack in a middle position for each result type
    /// (Req 4.3).
    /// </summary>
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    public void EmptyPack_BindingRuleAcceptsEmptyMiddlePack(string resultType)
    {
        Assert.True(VerifyBindingRuleEmptyMiddle(resultType));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static string DefaultLiteral(string typeName)
    {
        return typeName switch
        {
            "int" => "0",
            "string" => "\"\"",
            "bool" => "false",
            _ => "0",
        };
    }

    /// <summary>
    /// Returns a minimal parsed SyntaxNode for diagnostic location reporting in direct rule calls.
    /// </summary>
    private static Storm.Runtime.SyntaxNode GetDummyNode()
    {
        if(cachedDummyNode is not null)
        {
            return cachedDummyNode;
        }

        var lexer = new MistLexer();
        var parser = new MistParser();
        var bag = new DiagnosticBag();
        var sourceText = new SourceText("dummy.mist", "class X { }");
        var tokens = lexer.Lex(sourceText, bag);
        var result = parser.Parse(tokens, bag);
        cachedDummyNode = result.Root!;
        return cachedDummyNode;
    }

    private static Storm.Runtime.SyntaxNode? cachedDummyNode;
}