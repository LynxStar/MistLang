using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Integration;

/// <summary>
/// Task 16 of the Mist Standard Library spec: compile a user-defined
/// <c>Vector3</c> struct with an <c>operator +</c> declaration under the
/// <see cref="TypeCheckerAnalyzer.UseOperatorOverloadResolution"/> feature
/// flag and verify that operator overload resolution selects the user's
/// operator, types the <c>v1 + v2</c> expression as <c>Vector3</c>, and
/// emits no diagnostics.
///
/// The production flag stays <c>false</c>; this test flips it locally inside
/// a <c>try/finally</c> so other tests and the driver continue on the old
/// path. Today the flag gates nothing — it is reserved for Stage 4's
/// migration — but flipping it here mirrors the future call pattern so that
/// when Stage 4 lands, this test still documents the expected behavior.
///
/// Per the Task 16 nuance, the test does NOT run the full analyzer pipeline:
/// the existing <c>CheckAdditiveNode</c> (old path) would reject
/// <c>v1 + v2</c> for non-numeric operand types because it does not know
/// about user-defined operators. Instead, the test builds the semantic model
/// directly and calls <see cref="TypeCheckerAnalyzer.ResolveBinaryOperator"/>
/// on the <c>v1 + v2</c> <see cref="AdditiveNode"/>. This mirrors Task 14.1's
/// test pattern and isolates the new operator-resolution path from old-path
/// diagnostics.
///
/// Validates:
/// * Requirement 4.1 — grammar/semantic acceptance of a user
///   <c>operator +</c> declaration on an ordinary struct.
/// * Requirement 4.6 — operator declarations are registered as static
///   methods discoverable by overload resolution.
/// * Requirement 6.1 — candidates are gathered from the operand types.
/// * Requirement 6.3 — on <c>Success</c>, the selected symbol is recorded
///   and the expression type is the operator's return type.
/// </summary>
public class Vector3OperatorIntegrationTest
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Lexes, parses, and semantically analyses <paramref name="source"/>.
    /// Returns the built semantic model and its single parsed AST root.
    /// Asserts that each pipeline stage produces no errors — the test targets
    /// operator overload resolution in isolation, so upstream failures would
    /// indicate a fixture problem rather than a spec violation.
    /// </summary>
    private (ISemanticModel Model, SyntaxNode Ast) BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        Assert.False(semBag.HasErrors,
            "Semantic model build should not produce errors for a well-formed Vector3 program");

        return (model, parseResult.Root!);
    }

    /// <summary>
    /// Walks <paramref name="root"/> depth-first and yields every
    /// <see cref="AdditiveNode"/> that applies an actual binary operator
    /// (i.e., has a non-null <c>Right</c> distinct from <c>Left</c> and a
    /// real operator token). Pass-through additive nodes — those that only
    /// delegate to a <c>Multiplicative</c> without an operator — are
    /// skipped.
    /// </summary>
    private static IEnumerable<AdditiveNode> FindAdditiveOperatorNodes(SyntaxNode root)
    {
        var stack = new Stack<SyntaxNode>();
        stack.Push(root);
        while(stack.Count > 0)
        {
            var node = stack.Pop();
            if(node is AdditiveNode add
                && add.Right is not null
                && !ReferenceEquals(add.Right, add.Left)
                && add.Operator.Lexeme is not null)
            {
                yield return add;
            }

            foreach(var child in node.Children())
            {
                if(child is not null)
                {
                    stack.Push(child);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Task 16 — Vector3 with float fields, a user operator +, and a
    // `v1 + v2` local-variable call site resolves successfully under the
    // feature flag.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compile a <c>Vector3</c> struct with <c>public float x, y, z</c>,
    /// a parameterized constructor, and a user-defined
    /// <c>public static Vector3 operator +(Vector3 a, Vector3 b)</c>. A
    /// separate static method declares two local <c>Vector3</c> variables
    /// (<c>v1</c> and <c>v2</c>) and computes <c>v1 + v2</c>. Running
    /// <see cref="TypeCheckerAnalyzer.ResolveBinaryOperator"/> on that
    /// <see cref="AdditiveNode"/> under the
    /// <see cref="TypeCheckerAnalyzer.UseOperatorOverloadResolution"/>
    /// feature flag must:
    /// <list type="bullet">
    ///   <item>Record the user's <c>operator +</c> as the resolved operator
    ///         via <c>RecordResolvedOperator</c>.</item>
    ///   <item>Set the expression type of the <c>v1 + v2</c> node to
    ///         <c>Vector3</c>.</item>
    ///   <item>Emit no diagnostics from resolution itself.</item>
    /// </list>
    /// The feature flag is flipped to <c>true</c> inside a
    /// <c>try/finally</c> and restored to <c>false</c> on exit so that
    /// subsequent tests and the driver continue on the old path.
    /// </summary>
    [Fact]
    public void Task16_Vector3_UserOperatorPlus_ResolvesUnderFeatureFlag()
    {
        // A Vector3 struct with the full field set from the Task 16 spec
        // (`public float x; public float y; public float z;`), a
        // constructor for instantiation at the call site, and the user's
        // `operator +`. `Add` is the call-site host: it declares `v1` and
        // `v2` as local Vector3 variables so that `v1 + v2` is a
        // variable-operand application (not a parameter application).
        //
        // The operator body just returns `a`. The exact arithmetic is not
        // under test — Task 16 cares about resolution, recording, and
        // typing. Returning `a` avoids depending on initializer syntax and
        // keeps the fixture minimal.
        var source =
            "public struct Vector3 { " +
            "  public float x; " +
            "  public float y; " +
            "  public float z; " +
            "  public Vector3(float x, float y, float z) { " +
            "    this.x = x; " +
            "    this.y = y; " +
            "    this.z = z; " +
            "  } " +
            "  public static Vector3 operator +(Vector3 a, Vector3 b) { return a; } " +
            "  public static Vector3 Add() { " +
            "    Vector3 v1 = new Vector3(1.0f, 2.0f, 3.0f); " +
            "    Vector3 v2 = new Vector3(4.0f, 5.0f, 6.0f); " +
            "    return v1 + v2; " +
            "  } " +
            "}";

        // Flip the feature flag for the duration of this test. The production
        // flag is `false`; other tests and the driver continue on the old
        // path. `try/finally` ensures restoration even if an assertion fails.
        var originalFlag = TypeCheckerAnalyzer.UseOperatorOverloadResolution;
        TypeCheckerAnalyzer.UseOperatorOverloadResolution = true;
        try
        {
            var (model, ast) = BuildModel(source);

            // Locate the AdditiveNode for `v1 + v2` (the only `+` in the
            // program). Multiple matches would mean the fixture drifted.
            var addNode = FindAdditiveOperatorNodes(ast).Single();
            Assert.Equal("+", addNode.Operator.Lexeme);

            // Locate the user-declared Vector3 type and its operator+ symbol.
            // Both are needed to state the expected resolution outcome.
            var vectorSymbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == "Vector3" && s.Kind == SymbolKind.Struct);
            Assert.NotNull(vectorSymbol);
            var vectorType = vectorSymbol!.DeclaredType as StructType;
            Assert.NotNull(vectorType);

            var declaredOperator = vectorType!.MemberScope.Members
                .FirstOrDefault(m => m.Name == "operator+" && m.Kind == SymbolKind.Method);
            Assert.NotNull(declaredOperator);

            // Pre-condition: v1 and v2 both resolve to Vector3 through the
            // local-symbol lookup populated during semantic model build. If
            // this fails we are testing the wrong thing.
            Assert.Same(vectorType, model.GetTypeOf(addNode.Left));
            Assert.Same(vectorType, model.GetTypeOf(addNode.Right));

            // Act — invoke the operator-resolution helper directly. Task
            // 16's nuance: do NOT run full Analyze, because the existing
            // CheckAdditiveNode (old path) does not know about user
            // operators and would pollute the diagnostic bag with
            // MIST_TC_0003. Calling ResolveBinaryOperator directly
            // exercises the new path in isolation.
            var diagnostics = new DiagnosticBag();
            TypeCheckerAnalyzer.ResolveBinaryOperator(
                op: "+",
                left: addNode.Left,
                right: addNode.Right,
                surrounding: addNode,
                noApplicableDiagnosticId: "MIST_TC_0003",
                model: model,
                diagnostics: diagnostics);

            // Assert — no diagnostics from resolution, the operator is
            // recorded, and the expression type is Vector3 (the operator's
            // return type).
            Assert.False(diagnostics.HasErrors,
                "ResolveBinaryOperator on a valid user operator should emit no diagnostic.");

            var recorded = model.GetResolvedOperator(addNode);
            Assert.NotNull(recorded);
            Assert.Same(declaredOperator, recorded);

            Assert.Same(vectorType, model.GetTypeOf(addNode));
        }
        finally
        {
            TypeCheckerAnalyzer.UseOperatorOverloadResolution = originalFlag;
        }
    }
}