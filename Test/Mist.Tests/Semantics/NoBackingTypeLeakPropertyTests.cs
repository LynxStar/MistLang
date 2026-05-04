// Task 47 of the Mist Standard Library spec: property-based test that
// no backing type leaks into the semantic model. Design § *Correctness
// Properties → Property 7*:
//
//   The generator produces Mist source that exercises various features.
//   The test builds the ISemanticModel, then traverses every reachable
//   IType and asserts each is either a user-declared Mist type, a
//   Standard.* type, or an ambient synthesized type. Failure if any
//   reachable IType's DisplayName or ContainingNamespace matches a
//   CLR-type pattern (System.*).
//
// **Validates: Requirements 9.6, 9.9, 9.10, 12.10, 12.8**

using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for Task 47 of the Mist Standard Library spec:
/// no backing type leaks into the semantic model.
///
/// Design § *Correctness Properties → Property 7*.
///
/// For each generated test case the property test:
/// <list type="number">
///   <item>Generates a Mist program that exercises Console.WriteLine,
///         string literals, numeric literals, and arithmetic.</item>
///   <item>Builds the semantic model with stdlib and ambient resolver.</item>
///   <item>Runs the type checker.</item>
///   <item>Walks all symbols and expression types, asserting no
///         System.* types leak through.</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class NoBackingTypeLeakPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Known ambient types ─────────────────────────────────────────────

    /// <summary>
    /// Ambient types (Console, Math, Convert) are synthesized by the
    /// CSharpAmbientTypeResolver. Their ContainingNamespace is "System"
    /// because that's where they live in the CLR, but they are expected
    /// ambient types — not leaks. We allow these specific type names.
    /// </summary>
    private static readonly HashSet<string> AllowedAmbientTypeNames = new()
    {
        "Console", "Math", "Convert"
    };

    // ─── Code fragments for generation ───────────────────────────────────

    /// <summary>
    /// Fragments of Mist code that exercise different features. Each
    /// fragment is a statement or set of statements that can be placed
    /// inside a method body.
    /// </summary>
    private static readonly string[] CodeFragments =
    {
        // String literal and Console.WriteLine
        "Console.WriteLine(\"hello\");",
        // Numeric literal
        "int x = 42;",
        // Arithmetic
        "int a = 1; int b = 2; int c = a + b;",
        // Comparison
        "int p = 10; int q = 20; bool r = p < q;",
        // String variable
        "string s = \"world\";",
        // Bool literal
        "bool flag = true;",
        // Multiple operations
        "int m = 3; int n = 4; int o = m * n; bool eq = o == 12;",
        // Nested arithmetic
        "int i = 5; int j = 6; int k = (i + j) * 2;",
        // String concat
        "string g = \"foo\"; string h = \"bar\"; string gh = g + h;",
        // Bool operations
        "bool t = true; bool f = false; bool res = t && f;",
        // Negation
        "int neg = 0 - 42;",
        // Equality
        "int e1 = 10; int e2 = 10; bool same = e1 == e2;",
    };

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// A single generated scenario: a selection of code fragments to
    /// include in the test program.
    /// </summary>
#pragma warning disable CA1034
    public record LeakScenario(IReadOnlyList<int> FragmentIndices)
    {
        public override string ToString()
        {
            var indices = string.Join(",", FragmentIndices);
            return $"Fragments=[{indices}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="LeakScenario"/> instances by picking 1–4
    /// random code fragments.
    /// </summary>
    public static Arbitrary<LeakScenario> ArbitraryLeakScenarios()
    {
        var gen =
            from count in Gen.Choose(1, 4)
            from indices in Gen.ArrayOf(
                Gen.Choose(0, CodeFragments.Length - 1),
                count)
            select new LeakScenario(indices.ToList());

        return gen.ToArbitrary();
    }

    // ─── Source generation ────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid Mist source program from a scenario.
    /// Variable names are suffixed with the fragment index to avoid
    /// duplicate declarations when multiple fragments are combined.
    /// </summary>
    private static string GenerateSource(LeakScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("namespace TestNs;");
        sb.AppendLine("public class Program {");
        sb.AppendLine("    public static void Main(string[] args) {");

        for (var i = 0; i < scenario.FragmentIndices.Count; i++)
        {
            var idx = scenario.FragmentIndices[i];
            // Generate fragment with unique variable names using suffix
            var fragment = GenerateFragment(idx, i);
            sb.AppendLine($"        {fragment}");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a code fragment with unique variable names using a suffix.
    /// </summary>
    private static string GenerateFragment(int fragmentIndex, int suffix)
    {
        var s = suffix.ToString();
        return fragmentIndex switch
        {
            0 => $"Console.WriteLine(\"hello{s}\");",
            1 => $"int x{s} = 42;",
            2 => $"int a{s} = 1; int b{s} = 2; int c{s} = a{s} + b{s};",
            3 => $"int p{s} = 10; int q{s} = 20; bool r{s} = p{s} < q{s};",
            4 => $"string s{s} = \"world\";",
            5 => $"bool flag{s} = true;",
            6 => $"int m{s} = 3; int n{s} = 4; int o{s} = m{s} * n{s}; bool eq{s} = o{s} == 12;",
            7 => $"int i{s} = 5; int j{s} = 6; int k{s} = (i{s} + j{s}) * 2;",
            8 => $"string g{s} = \"foo\"; string h{s} = \"bar\"; string gh{s} = g{s} + h{s};",
            9 => $"bool t{s} = true; bool f{s} = false; bool res{s} = t{s} && f{s};",
            10 => $"int neg{s} = 0 - 42;",
            11 => $"int e1_{s} = 10; int e2_{s} = 10; bool same{s} = e1_{s} == e2_{s};",
            _ => $"int def{s} = 0;",
        };
    }

    // ─── Semantic model building ─────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model with stdlib and ambient resolver loaded,
    /// runs the type checker, and returns the model and diagnostics.
    /// </summary>
    private static (ISemanticModel Model, DiagnosticBag Diagnostics) BuildAndTypeCheck(string userSource)
    {
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", userSource);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if (userParseResult.Root is null)
            throw new InvalidOperationException("Failed to parse user source");

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);

        // Build with ambient resolver for Console, Math, Convert
        var ambientResolver = new CSharpAmbientTypeResolver();
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: ambientResolver,
            namespaceRoot: "TestNs",
            projectFolder: "/test",
            diagnostics);

        // Run the type checker
        var analyzer = new TypeCheckerAnalyzer();
        analyzer.Analyze(model, diagnostics);

        return (model, diagnostics);
    }

    // ─── Type leak checking ──────────────────────────────────────────────

    /// <summary>
    /// Checks whether a type is a CLR leak (System.* namespace that is
    /// not an allowed ambient type).
    /// </summary>
    private static bool IsClrLeak(IType type)
    {
        // Phase B will reintroduce UnknownType as the user-facing top type, distinct from this ErrorType sentinel.
        // Void, ErrorType, TypeParameterType, ArrayType are fine
        if (type.IsVoid() || type is ErrorType or TypeParameterType or ArrayType)
            return false;

        string? containingNamespace = null;
        var displayName = type.DisplayName;

        if (type is ClassType ct)
            containingNamespace = ct.ContainingNamespace;
        else if (type is StructType st)
            containingNamespace = st.ContainingNamespace;

        // Check if the type's namespace starts with "System"
        if (containingNamespace is not null && containingNamespace.StartsWith("System"))
        {
            // Allow known ambient types (Console, Math, Convert)
            if (AllowedAmbientTypeNames.Contains(displayName))
                return false;

            // This is a CLR type leak
            return true;
        }

        // Check if the display name itself looks like a CLR type
        if (displayName.StartsWith("System."))
            return true;

        return false;
    }

    /// <summary>
    /// Collects all reachable types from the semantic model: symbol
    /// declared types, expression types, and member parameter/return types.
    /// Returns a list of (description, type) pairs for diagnostics.
    /// </summary>
    private static List<(string Description, IType Type)> CollectAllReachableTypes(
        ISemanticModel model)
    {
        var result = new List<(string, IType)>();

        // 1. All symbol declared types
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.DeclaredType is { } declType)
            {
                result.Add(($"Symbol '{symbol.Name}' (kind={symbol.Kind}) declared type", declType));

                // Check member types for class/struct types
                if (declType is ClassType ct)
                {
                    foreach (var member in ct.MemberScope.Members)
                    {
                        if (member.DeclaredType is { } memberType)
                        {
                            result.Add(($"Member '{ct.DisplayName}.{member.Name}' declared type", memberType));
                        }
                    }
                }
                else if (declType is StructType st)
                {
                    foreach (var member in st.MemberScope.Members)
                    {
                        if (member.DeclaredType is { } memberType)
                        {
                            result.Add(($"Member '{st.DisplayName}.{member.Name}' declared type", memberType));
                        }
                    }
                }
            }
        }

        // 2. Walk AST nodes for expression types (user AST only — last one)
        var userAst = model.Asts[^1];
        CollectExpressionTypes(userAst, model, result);

        return result;
    }

    /// <summary>
    /// Recursively walks AST nodes and collects expression types.
    /// Uses a stack-based approach with null checks for safety.
    /// </summary>
    private static void CollectExpressionTypes(
        Storm.Runtime.SyntaxNode root,
        ISemanticModel model,
        List<(string, IType)> result)
    {
        var stack = new Stack<Storm.Runtime.SyntaxNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            try
            {
                var exprType = model.GetTypeOf(node);
                if (exprType is not null)
                {
                    result.Add(($"Expression node {node.GetType().Name} type", exprType));
                }
            }
            catch
            {
                // GetTypeOf may throw for nodes not tracked in the model;
                // skip them safely.
            }

            foreach (var child in node.Children())
            {
                if (child is not null)
                    stack.Push(child);
            }
        }
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: No backing type leaks into the semantic model. Every
    /// reachable IType is either a user-declared Mist type, a Standard.*
    /// type, or an allowed ambient synthesized type (Console, Math, Convert).
    /// No System.* CLR types should appear.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 9.6, 9.9, 9.10, 12.10, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(NoBackingTypeLeakPropertyTests) })]
    public Property NoBackingTypeLeak_InSemanticModel(LeakScenario scenario)
    {
        {
            var source = GenerateSource(scenario);
            var (model, diagnostics) = BuildAndTypeCheck(source);

            // We allow type-checker errors (e.g., unresolved members) but
            // still check all types that were resolved.
            var fatalErrors = diagnostics.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error
                            && !d.Id.Value.StartsWith("MIST_TC")
                            && !d.Id.Value.StartsWith("MIST_SM"))
                .ToList();

            if (fatalErrors.Count > 0)
            {
                return false.ToProperty()
                    .Label($"Fatal build errors: {string.Join("; ", fatalErrors.Select(e => $"[{e.Id.Value}] {e.Message}"))}\nSource:\n{source}");
            }

            // Collect all reachable types
            var allTypes = CollectAllReachableTypes(model);

            // Check each type for CLR leaks
            foreach (var (description, type) in allTypes)
            {
                if (IsClrLeak(type))
                {
                    string? ns = null;
                    if (type is ClassType ct) ns = ct.ContainingNamespace;
                    else if (type is StructType st) ns = st.ContainingNamespace;

                    return false.ToProperty()
                        .Label($"CLR type leak detected!\n" +
                               $"  Location: {description}\n" +
                               $"  Type: {type.DisplayName}\n" +
                               $"  Namespace: {ns ?? "(none)"}\n" +
                               $"Source:\n{source}");
                }
            }

            return true.ToProperty()
                .Label($"Checked {allTypes.Count} reachable types for scenario: {scenario}");
        }
    }
}
