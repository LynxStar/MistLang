// Task 45 of the Mist Standard Library spec: property-based test that
// every operator expression is typed via OverloadResolver. Design §
// *Correctness Properties → Property 3*:
//
//   For any well-typed operator expression, the symbol recorded via
//   RecordResolvedOperator equals the symbol returned by a direct call
//   to OverloadResolver.Resolve on the same candidate set and argument
//   types.
//
// **Validates: Requirements 6.1, 6.2, 6.3, 6.7, 6.8, 6.9, 12.8**

using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
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
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for Task 45 of the Mist Standard Library spec:
/// every operator expression is typed via OverloadResolver.
///
/// Design § *Correctness Properties → Property 3*.
///
/// For each generated test case the property test:
/// <list type="number">
///   <item>Generates a Mist program with int arithmetic expressions
///         using a randomly chosen binary operator.</item>
///   <item>Builds the semantic model with stdlib loaded.</item>
///   <item>Runs the type checker.</item>
///   <item>For each operator expression node found in the AST, verifies
///         that GetResolvedOperator returns a symbol that matches what
///         OverloadResolver.Resolve would select for the same
///         candidates and argument types.</item>
/// </list>
///
/// Runs with <c>MaxTest = 100</c> per Requirement 12.8.
/// </summary>
public class OperatorResolutionConsistencyPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Operators to test ───────────────────────────────────────────────

    /// <summary>
    /// Binary operators that produce int results when applied to int operands.
    /// </summary>
    private static readonly string[] ArithmeticOps = { "+", "-", "*" };

    /// <summary>
    /// Binary operators that produce bool results when applied to int operands.
    /// </summary>
    private static readonly string[] ComparisonOps = { "<", ">", "<=", ">=", "==", "!=" };

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// A single generated scenario: a binary operator and two small int
    /// literal values used as operands.
    /// </summary>
#pragma warning disable CA1034
    public record OperatorScenario(string Op, int A, int B, bool IsComparison)
    {
        public override string ToString() => $"int: {A} {Op} {B} (comparison={IsComparison})";
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Generates <see cref="OperatorScenario"/> instances with small
    /// positive operand values and a randomly chosen operator.
    /// </summary>
    public static Arbitrary<OperatorScenario> ArbitraryOperatorScenarios()
    {
        var allOps = ArithmeticOps.Concat(ComparisonOps).ToArray();

        var gen =
            from op in Gen.Elements(allOps)
            from a in Gen.Choose(1, 50)
            from b in Gen.Choose(1, 50)
            select new OperatorScenario(op, a, b, ComparisonOps.Contains(op));

        return gen.ToArbitrary();
    }

    // ─── Source generation ────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid Mist source program that uses the given operator
    /// on two int variables.
    /// </summary>
    private static string GenerateSource(OperatorScenario scenario)
    {
        var resultType = scenario.IsComparison ? "bool" : "int";

        return $$"""
            namespace TestNs;

            public class Program
            {
                public static void Main(string[] args)
                {
                    int a = {{scenario.A}};
                    int b = {{scenario.B}};
                    {{resultType}} c = a {{scenario.Op}} b;
                }
            }
            """;
    }

    // ─── Semantic model building ─────────────────────────────────────────

    /// <summary>
    /// Builds a semantic model with stdlib loaded, runs the type checker,
    /// and returns the model and diagnostics.
    /// </summary>
    private static (ISemanticModel Model, DiagnosticBag Diagnostics) BuildAndTypeCheck(string userSource)
    {
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", userSource);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            throw new InvalidOperationException("Failed to parse user source");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestNs",
            projectFolder: "/test",
            diagnostics);

        // Run the type checker
        var analyzer = new TypeCheckerAnalyzer();
        analyzer.Analyze(model, diagnostics);

        return (model, diagnostics);
    }

    /// <summary>
    /// Finds the Standard.Int32 type in the semantic model.
    /// </summary>
    private static IType? FindInt32Type(ISemanticModel model)
    {
        return model.AllSymbols
            .Where(s => s.Name == "Int32"
                        && s.Kind == SymbolKind.Struct
                        && s.DeclaredType is StructType st
                        && st.ContainingNamespace == "Standard")
            .Select(s => s.DeclaredType!)
            .FirstOrDefault();
    }

    /// <summary>
    /// Collects operator member candidates from a type for a given operator name.
    /// </summary>
    private static List<ISymbol> GetOperatorMembers(IType type, string opName)
    {
        var members = new List<ISymbol>();

        if(type is StructType st)
        {
            members.AddRange(st.MemberScope.Members
                .Where(m => m.Name == opName && m.Kind == SymbolKind.Method));
        }
        else if(type is ClassType ct)
        {
            members.AddRange(ct.MemberScope.Members
                .Where(m => m.Name == opName && m.Kind == SymbolKind.Method));
        }

        return members;
    }

    // ─── AST walking ─────────────────────────────────────────────────────

    /// <summary>
    /// Recursively walks the AST to find all operator expression nodes
    /// that have a resolved operator recorded. Uses a safe null-checking
    /// walk to handle nullable children in the generated AST.
    /// </summary>
    private static List<SyntaxNode> FindNodesWithResolvedOperators(
        SyntaxNode root, ISemanticModel model)
    {
        var result = new List<SyntaxNode>();
        var stack = new Stack<SyntaxNode>();
        stack.Push(root);

        while(stack.Count > 0)
        {
            var node = stack.Pop();

            // Check if this node has a resolved operator recorded
            if(model.GetResolvedOperator(node) is not null)
            {
                result.Add(node);
            }

            foreach(var child in node.Children())
            {
                if(child is not null)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    // ─── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any well-typed operator expression, the symbol
    /// recorded via RecordResolvedOperator equals the symbol returned by
    /// a direct call to OverloadResolver.Resolve on the same candidate
    /// set and argument types.
    ///
    /// Runs ≥100 iterations per Requirement 12.8.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.7, 6.8, 6.9, 12.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OperatorResolutionConsistencyPropertyTests) })]
    public Property OperatorExpressions_TypedViaOverloadResolver(
        OperatorScenario scenario)
    {
        {
            var source = GenerateSource(scenario);
            var (model, diagnostics) = BuildAndTypeCheck(source);

            // Check for fatal errors that would prevent meaningful assertions.
            // Allow MIST_TC errors since the type checker still records operators.
            var errors = diagnostics.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error
                            && !d.Id.Value.StartsWith("MIST_TC"))
                .ToList();

            if(errors.Count > 0)
            {
                return false.ToProperty()
                    .Label($"Build errors: {string.Join("; ", errors.Select(e => $"[{e.Id.Value}] {e.Message}"))}\nSource:\n{source}");
            }

            // Find the Int32 type
            var int32Type = FindInt32Type(model);
            if(int32Type is null)
            {
                return false.ToProperty()
                    .Label("Could not find Standard.Int32 type in the semantic model.");
            }

            // Find operator expression nodes in the user's AST (last AST)
            var userAst = model.Asts[^1];
            var operatorNodes = FindNodesWithResolvedOperators(userAst, model);

            if(operatorNodes.Count == 0)
            {
                return false.ToProperty()
                    .Label($"No operator expression nodes with resolved operators found.\nSource:\n{source}");
            }

            // For each operator node, verify the recorded resolved operator
            // matches what OverloadResolver.Resolve would return.
            foreach(var opNode in operatorNodes)
            {
                var recordedOp = model.GetResolvedOperator(opNode)!;
                var opName = recordedOp.Name; // e.g., "operator+"

                // Both operands are int, so gather candidates from Int32
                var candidates = GetOperatorMembers(int32Type, opName);

                if(candidates.Count == 0)
                {
                    return false.ToProperty()
                        .Label($"No candidates found for {opName} on Int32.\nSource:\n{source}");
                }

                // Resolve via OverloadResolver with the same argument types
                var argTypes = new IType[] { int32Type, int32Type };
                var resolverResult = OverloadResolver.Resolve(
                    candidates, argTypes, model);

                if(resolverResult.Status != OverloadResolutionStatus.Success)
                {
                    return false.ToProperty()
                        .Label($"OverloadResolver.Resolve returned {resolverResult.Status} for {opName}.\nSource:\n{source}");
                }

                // The recorded operator must be the same symbol as what
                // OverloadResolver would select.
                if(!ReferenceEquals(recordedOp, resolverResult.BestCandidate))
                {
                    return false.ToProperty()
                        .Label($"Recorded operator for {opName} differs from OverloadResolver result.\n" +
                               $"Recorded: {recordedOp.Name} (hash={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(recordedOp)})\n" +
                               $"Resolver: {resolverResult.BestCandidate!.Name} (hash={System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(resolverResult.BestCandidate)})\n" +
                               $"Source:\n{source}");
                }
            }

            return true.ToProperty()
                .Label($"All {operatorNodes.Count} operator nodes verified for scenario: {scenario}");
        }
    }
}