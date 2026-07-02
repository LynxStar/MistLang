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

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for Task 14 of the Mist Standard Library spec: the
/// <see cref="TypeCheckerAnalyzer.ResolveBinaryOperator"/> helper resolves
/// binary operator expressions by gathering operator-member candidates from
/// both operand types and dispatching through
/// <see cref="OverloadResolver.Resolve"/>.
///
/// The helper is exercised directly here (rather than via <c>Analyze</c>)
/// because Task 14 deliberately leaves the existing <c>Check*Node</c> methods
/// unchanged — the new path is gated by
/// <see cref="TypeCheckerAnalyzer.UseOperatorOverloadResolution"/>, which
/// remains <c>false</c> for production traffic until Stage 4 of the migration.
/// Calling the helper directly is the canonical way to verify its contract
/// without waiting for the flag flip.
///
/// Validates:
/// * Requirement 6.1 — candidate gathering from operand types.
/// * Requirement 6.2 — overload-resolution dispatch.
/// * Requirement 6.3 — <c>Success</c> records the selected symbol and sets
///   the expression type.
/// * Requirement 6.4 — <c>NoApplicableCandidate</c> emits the diagnostic ID
///   the caller specifies (e.g. <c>MIST_TC_0003</c>).
/// * Requirement 6.5 — <c>AmbiguousCall</c> emits <c>MIST_TC_0032</c>.
/// * Requirement 6.6 — resolution is routed through the same
///   <see cref="OverloadResolver.Resolve"/> the method-call path uses.
/// </summary>
public class ResolveBinaryOperatorTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Lexes, parses, and semantically analyses <paramref name="source"/>.
    /// Returns the built semantic model and its single parsed AST root.
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

        // Semantic model construction itself must succeed — the tests target
        // operator resolution in isolation, not upstream semantic errors.
        Assert.False(semBag.HasErrors,
            "Semantic model build should not produce errors for a well-formed test program");

        return (model, parseResult.Root!);
    }

    /// <summary>
    /// Recursively walks <paramref name="root"/> and returns every
    /// <see cref="AdditiveNode"/> that carries a non-default
    /// <see cref="AdditiveNode.Operator"/> token (i.e., an actual <c>+</c>
    /// or <c>-</c> application, not a pass-through to <c>Multiplicative</c>).
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
    // Sub-task 14.1 — Success: user-declared Vector3 + operator resolves,
    // records the symbol, sets the expression type.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A user-defined <c>Vector3</c> struct with <c>operator +(Vector3, Vector3)</c>
    /// declared on it. Calling <see cref="TypeCheckerAnalyzer.ResolveBinaryOperator"/>
    /// on a <c>v1 + v2</c> expression where both operands are <c>Vector3</c>
    /// must:
    /// <list type="bullet">
    ///   <item>Select the user's <c>operator +</c> as the best candidate.</item>
    ///   <item>Record that selection via <c>RecordResolvedOperator</c> so the
    ///         emitter can later retrieve it through
    ///         <c>GetResolvedOperator</c>.</item>
    ///   <item>Set the expression type of the surrounding <see cref="AdditiveNode"/>
    ///         to <c>Vector3</c> — the operator's declared return type.</item>
    ///   <item>Emit no diagnostics.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Task14_1_UserDefinedVector3OperatorPlus_ResolvesSuccessfully()
    {
        // A struct with a binary `+` operator and a static method containing
        // a call site that applies it. The method body gives us a real
        // AdditiveNode in the AST whose Left and Right nodes carry types.
        var source =
            "public struct Vector3 { " +
            "  public static Vector3 operator +(Vector3 a, Vector3 b) { return a; } " +
            "  public static Vector3 Add(Vector3 x, Vector3 y) { return x + y; } " +
            "}";

        var (model, ast) = BuildModel(source);

        // Locate the AdditiveNode for `x + y` (the only `+` in the program).
        var addNode = FindAdditiveOperatorNodes(ast).Single();
        Assert.Equal("+", addNode.Operator.Lexeme);

        // Locate the user-declared Vector3 type and its operator symbol — used
        // both to seed the assertions and to sanity-check the fixture.
        var vectorSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Vector3" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(vectorSymbol);
        var vectorType = vectorSymbol!.DeclaredType as StructType;
        Assert.NotNull(vectorType);

        var declaredOperator = vectorType!.MemberScope.Members
            .FirstOrDefault(m => m.Name == "operator+" && m.Kind == SymbolKind.Method);
        Assert.NotNull(declaredOperator);

        // Pre-condition: the operands' types must already be populated by the
        // semantic model's Pass 2 expression-type pass. If this fails we would
        // be testing the wrong thing.
        Assert.Same(vectorType, model.GetTypeOf(addNode.Left));
        Assert.Same(vectorType, model.GetTypeOf(addNode.Right));

        // Act — call the helper under test.
        var diagnostics = new DiagnosticBag();
        TypeCheckerAnalyzer.ResolveBinaryOperator(
            op: "+",
            left: addNode.Left,
            right: addNode.Right,
            surrounding: addNode,
            noApplicableDescriptor: TypeCheckDiagnostics.OperatorNotApplicableArithmetic,
            model: model,
            diagnostics: diagnostics);

        // Assert — Success: no diagnostics, the operator is recorded, and the
        // expression type matches the operator's return type (Vector3).
        Assert.False(diagnostics.HasErrors,
            "ResolveBinaryOperator on a valid user operator should not emit any diagnostic.");

        var recorded = model.GetResolvedOperator(addNode);
        Assert.NotNull(recorded);
        Assert.Same(declaredOperator, recorded);

        Assert.Same(vectorType, model.GetTypeOf(addNode));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sub-task 14.2 — NoApplicableCandidate: types with no operator emit
    // the diagnostic ID the caller specifies (e.g. MIST_TC_0003).
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two operand expressions of user-struct type that declares no
    /// operators. <see cref="TypeCheckerAnalyzer.ResolveBinaryOperator"/>
    /// must emit the caller-provided diagnostic ID (<c>MIST_TC_0003</c> for
    /// additive/multiplicative) and must NOT record any operator on the
    /// surrounding node.
    /// </summary>
    [Fact]
    public void Task14_2_NoApplicableOperator_EmitsMistTc0003()
    {
        // Struct `Bare` has no operators. The static method creates an add
        // expression whose operands are both `Bare`, so overload resolution
        // finds zero applicable candidates.
        //
        // Note: semantic model construction populates expression types
        // independently of operator resolution. The existing type checker
        // (old path) would reject `x + y` with a diagnostic; we bypass that
        // by calling ResolveBinaryOperator directly and verifying the new
        // path's behavior in isolation.
        var source =
            "public struct Bare { " +
            "  public static Bare Add(Bare x, Bare y) { return x + y; } " +
            "}";

        var (model, ast) = BuildModel(source);

        var addNode = FindAdditiveOperatorNodes(ast).Single();

        var bareSymbol = model.AllSymbols
            .First(s => s.Name == "Bare" && s.Kind == SymbolKind.Struct);
        var bareType = bareSymbol.DeclaredType as StructType;
        Assert.NotNull(bareType);

        // Sanity check — Bare declares no `operator+`.
        Assert.DoesNotContain(
            bareType!.MemberScope.Members,
            m => m.Name == "operator+");

        // Act.
        var diagnostics = new DiagnosticBag();
        TypeCheckerAnalyzer.ResolveBinaryOperator(
            op: "+",
            left: addNode.Left,
            right: addNode.Right,
            surrounding: addNode,
            noApplicableDescriptor: TypeCheckDiagnostics.OperatorNotApplicableArithmetic,
            model: model,
            diagnostics: diagnostics);

        // Assert — exactly one diagnostic with the caller's ID.
        var errors = diagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        _ = Assert.Single(errors);
        Assert.Equal("MIST_TC_0003", errors[0].Id.Value);

        // No operator was recorded — resolution failed.
        Assert.Null(model.GetResolvedOperator(addNode));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sub-task 14.3 — Two equally-specific operators declared on distinct
    // operand types. Before Task 15 this scenario produced MIST_TC_0032.
    // After Task 15's left-operand precedence tiebreaker lands, the operator
    // declared on the LEFT operand's type wins — so the scenario now
    // resolves successfully to A's operator. The genuinely-ambiguous path
    // (MIST_TC_0032) requires candidates that share the same origin, which
    // is exercised by the Task 15.1 unit tests on OverloadResolver directly.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Types <c>A</c> and <c>B</c> each declare
    /// <c>operator +(A, B)</c>. Applied to operands of type <c>(A, B)</c>,
    /// both candidates are equally applicable with identical parameter
    /// signatures — no <c>MoreSpecific</c> relation holds between them.
    /// Task 15's tiebreaker (design § 5) prefers the candidate gathered from
    /// the LEFT operand's type, so <c>A.operator+</c> wins over
    /// <c>B.operator+</c>. This test pins that new behavior.
    /// </summary>
    [Fact]
    public void Task14_3_TwoEquallyApplicableOperators_LeftOriginTiebreakerSelectsA()
    {
        // A and B each declare an `operator +(A, B)`. The call site uses
        // operand types (A, B), so both operators match exactly with no
        // specificity difference between them.
        //
        // The `Caller` struct owns the call site `x + y`, giving us a real
        // AdditiveNode in the AST to hand to ResolveBinaryOperator.
        var source =
            "public struct B { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "} " +
            "public struct A { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "} " +
            "public struct Caller { " +
            "  public static A Combine(A x, B y) { return x + y; } " +
            "}";

        var (model, ast) = BuildModel(source);

        var addNode = FindAdditiveOperatorNodes(ast).Single();

        var aType = (model.AllSymbols.First(s => s.Name == "A" && s.Kind == SymbolKind.Struct)
            .DeclaredType as StructType)!;
        var bType = (model.AllSymbols.First(s => s.Name == "B" && s.Kind == SymbolKind.Struct)
            .DeclaredType as StructType)!;

        // Sanity checks — each type declares exactly one `operator+` with
        // signature (A, B). Two distinct symbols, same parameter types.
        var aOp = aType.MemberScope.Members.Single(m => m.Name == "operator+");
        var bOp = bType.MemberScope.Members.Single(m => m.Name == "operator+");
        Assert.NotSame(aOp, bOp);

        var aParams = ParameterSignature.Extract(aOp, model);
        var bParams = ParameterSignature.Extract(bOp, model);
        Assert.Same(aType, aParams[0]);
        Assert.Same(bType, aParams[1]);
        Assert.Same(aType, bParams[0]);
        Assert.Same(bType, bParams[1]);

        // The operand types at the call site are A and B in order.
        Assert.Same(aType, model.GetTypeOf(addNode.Left));
        Assert.Same(bType, model.GetTypeOf(addNode.Right));

        // Act.
        var diagnostics = new DiagnosticBag();
        TypeCheckerAnalyzer.ResolveBinaryOperator(
            op: "+",
            left: addNode.Left,
            right: addNode.Right,
            surrounding: addNode,
            noApplicableDescriptor: TypeCheckDiagnostics.OperatorNotApplicableArithmetic,
            model: model,
            diagnostics: diagnostics);

        // Assert — Task 15's left-origin tiebreaker selects A's operator,
        // because A is the left operand's type (origin 0) and B is the right
        // operand's type (origin 1). No diagnostic is emitted. The expression
        // type is the operator's return type, which is A.
        Assert.False(diagnostics.HasErrors,
            "Left-origin tiebreaker should resolve the call without diagnostics.");

        var recorded = model.GetResolvedOperator(addNode);
        Assert.NotNull(recorded);
        Assert.Same(aOp, recorded);

        Assert.Same(aType, model.GetTypeOf(addNode));
    }
}