// Generators for Mist stackalloc expression source strings.
// Produces random but structurally valid programs containing `stackalloc T[n]`
// expressions for property-based testing of parsing structure and round-trip.
// Feature: stack-only-types, Property 2: Stackalloc parsing structure and round-trip
// **Validates: Requirements 3.2, 3.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated stackalloc source string for property-based testing.
/// Carries the complete Mist program source text along with a human-readable
/// description and the syntactic position the stackalloc expression occupies.
/// </summary>
public sealed class StackallocSourceScenario
{
    /// <summary>The complete Mist program source string containing exactly one stackalloc expression.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public StackallocSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist programs containing a single <c>stackalloc T[n]</c>
/// expression. Generates random valid programs covering:
/// <list type="bullet">
///   <item>Element types: primitive/stampable type references (int, bool, char, float, ...)</item>
///   <item>Sizes: integer literals (known length) and runtime expressions (unknown length)</item>
///   <item>Syntactic positions (R3.6): local variable initializer, call argument,
///         right-hand side of an assignment</item>
/// </list>
/// Each generated program is a complete, parseable Mist source file with namespace,
/// class, and method wrapping. The programs are intended for the parser stage only
/// (they need not type-check), so undeclared identifiers used as runtime sizes are fine.
/// </summary>
public static class MistStackallocGenerator
{
    // ═══════════════════════════════════════════════════════════════════════
    // Pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] ElementTypes =
        ["int", "bool", "char", "float", "double", "long", "byte", "short", "uint"];

    private static readonly string[] LiteralSizes =
        ["0", "1", "2", "3", "4", "8", "16", "42", "100"];

    private static readonly string[] RuntimeSizes =
        ["n", "count", "len", "n + 1", "count * 2"];

    // ═══════════════════════════════════════════════════════════════════════
    // Fragment generators
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<string> GenElementType() => Gen.Elements(ElementTypes);

    /// <summary>Generates the bracketed size expression: a literal (known length) or runtime expression (unknown length).</summary>
    private static Gen<string> GenSize() =>
        Gen.OneOf(Gen.Elements(LiteralSizes), Gen.Elements(RuntimeSizes));

    /// <summary>Generates the bare <c>stackalloc T[n]</c> expression fragment.</summary>
    private static Gen<string> GenStackallocExpr() =>
        from type in GenElementType()
        from size in GenSize()
        select $"stackalloc {type}[{size}]";

    // ═══════════════════════════════════════════════════════════════════════
    // Positional scenario generators (R3.6)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>stackalloc used as a local variable initializer.</summary>
    private static Gen<StackallocSourceScenario> GenLocalInitializer() =>
        from expr in GenStackallocExpr()
        let body = $"var s = {expr};"
        select new StackallocSourceScenario(Wrap(body), $"local-initializer: {expr}");

    /// <summary>stackalloc used as a call argument.</summary>
    private static Gen<StackallocSourceScenario> GenCallArgument() =>
        from expr in GenStackallocExpr()
        let body = $"Consume({expr});"
        select new StackallocSourceScenario(Wrap(body), $"call-argument: {expr}");

    /// <summary>stackalloc used as the right-hand side of an assignment.</summary>
    private static Gen<StackallocSourceScenario> GenAssignmentRhs() =>
        from expr in GenStackallocExpr()
        let body = $"var dst = seed;\n        dst = {expr};"
        select new StackallocSourceScenario(Wrap(body), $"assignment-rhs: {expr}");

    // ═══════════════════════════════════════════════════════════════════════
    // Combined generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random stackalloc source scenario covering all three syntactic
    /// positions and a mix of literal and runtime sizes.
    /// </summary>
    public static Gen<StackallocSourceScenario> GenStackallocSource() =>
        Gen.OneOf(
            GenLocalInitializer(),
            GenCallArgument(),
            GenAssignmentRhs());

    // ═══════════════════════════════════════════════════════════════════════
    // Program wrapping
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps a body of statements in a complete Mist program with namespace, class,
    /// and a method taking a <c>seed</c> parameter (used by the assignment-rhs scenario).
    /// </summary>
    private static string Wrap(string body) =>
        "namespace TestNs;\n\npublic class Program\n{\n    public static void Run(int seed)\n    {\n        "
        + body + "\n    }\n}";

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registration
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FsCheck Arbitrary for <see cref="StackallocSourceScenario"/>.
    /// Register with <c>[Property(Arbitrary = new[] { typeof(MistStackallocGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<StackallocSourceScenario> ArbitraryStackallocSourceScenario() =>
        GenStackallocSource().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Malformed scenarios — missing/empty stackalloc size (Property 3)
    // Feature: stack-only-types, Property 3: Stackalloc missing/empty size is
    // rejected and recovered. **Validates: Requirements 3.4, 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Element-type references exercised by the missing/empty-size scenarios. Includes
    /// primitives, simple and qualified identifiers, and generic forms so the parser's
    /// recovery scan is exercised across the full shape of an <c>ElementTypeRef</c>.
    /// </summary>
    private static readonly string[] MalformedElementTypes =
        ["int", "bool", "char", "Foo", "Bar", "My.Ns.Widget", "List<int>", "Dictionary<int, string>"];

    private static Gen<string> GenMalformedElementType() => Gen.Elements(MalformedElementTypes);

    /// <summary>
    /// Generates a malformed stackalloc fragment: either <c>stackalloc T</c> (no bracketed
    /// size, R3.4) or <c>stackalloc T[]</c> (empty brackets, R3.5).
    /// </summary>
    private static Gen<(string Expr, string Kind)> GenMalformedStackallocExpr() =>
        from type in GenMalformedElementType()
        from emptyBrackets in Gen.Elements(false, true)
        select emptyBrackets
            ? ($"stackalloc {type}[]", "empty-brackets")
            : ($"stackalloc {type}", "no-brackets");

    /// <summary>Malformed stackalloc used as a local variable initializer.</summary>
    private static Gen<StackallocSourceScenario> GenMalformedLocalInitializer() =>
        from m in GenMalformedStackallocExpr()
        let body = $"var s = {m.Expr};"
        select new StackallocSourceScenario(Wrap(body), $"{m.Kind} local-initializer: {m.Expr}");

    /// <summary>Malformed stackalloc used as a call argument.</summary>
    private static Gen<StackallocSourceScenario> GenMalformedCallArgument() =>
        from m in GenMalformedStackallocExpr()
        let body = $"Consume({m.Expr});"
        select new StackallocSourceScenario(Wrap(body), $"{m.Kind} call-argument: {m.Expr}");

    /// <summary>Malformed stackalloc used as the right-hand side of an assignment.</summary>
    private static Gen<StackallocSourceScenario> GenMalformedAssignmentRhs() =>
        from m in GenMalformedStackallocExpr()
        let body = $"var dst = seed;\n        dst = {m.Expr};"
        select new StackallocSourceScenario(Wrap(body), $"{m.Kind} assignment-rhs: {m.Expr}");

    /// <summary>
    /// Generates a random malformed stackalloc source scenario (missing or empty size)
    /// across all three syntactic positions and element-type shapes.
    /// </summary>
    public static Gen<StackallocSourceScenario> GenMissingOrEmptySizeSource() =>
        Gen.OneOf(
            GenMalformedLocalInitializer(),
            GenMalformedCallArgument(),
            GenMalformedAssignmentRhs());
}
