// Feature: target-typed-new, Property 2: Parsing disambiguation
// **Validates: Requirements 1.4, 1.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario for parsing disambiguation of new expressions.
/// Includes a type name and an argument list to generate both fully-typed
/// and target-typed new expressions.
/// </summary>
public sealed class NewDisambiguationScenario
{
    public string TypeName { get; }
    public IReadOnlyList<string> Arguments { get; }

    public NewDisambiguationScenario(string typeName, IReadOnlyList<string> arguments)
    {
        TypeName = typeName;
        Arguments = arguments;
    }

    public override string ToString() =>
        $"TypeName={TypeName}, Args=[{string.Join(", ", Arguments)}]";
}

/// <summary>
/// Property-based tests for parsing disambiguation between fully-typed and target-typed new.
///
/// Property 2: For any valid type name and argument list, parsing <c>new TypeName(args)</c>
/// SHALL always produce an <see cref="ObjectCreationExpressionPrimaryNode"/>, never a
/// <see cref="TargetTypedNewExpressionPrimaryNode"/>. The target-typed alternative is only
/// selected when no type name is present between <c>new</c> and <c>(</c>.
/// </summary>
public class TargetTypedNewDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generator ──────────────────────────────────────────────────────────

    private static readonly string[] ValidTypeNames =
        ["Point", "Config", "Settings", "Options", "Data", "State", "Node", "Item", "Result", "Value"];

    private static readonly string[] ValidArgExprs =
        ["0", "1", "42", "100", "true", "false", "\"hello\"", "\"\""];

    public static Arbitrary<NewDisambiguationScenario> ArbitraryScenario()
    {
        var gen =
            from typeIdx in Gen.Choose(0, ValidTypeNames.Length - 1)
            from argCount in Gen.Choose(0, 3)
            from argIndices in Gen.ListOf(Gen.Choose(0, ValidArgExprs.Length - 1), argCount)
            let args = argIndices.Select(i => ValidArgExprs[i]).ToArray()
            select new NewDisambiguationScenario(ValidTypeNames[typeIdx], args);

        return gen.ToArbitrary();
    }

    // ─── Parsing helpers ────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return null;

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Builds source for a fully-typed new expression: <c>new TypeName(args)</c>.
    /// </summary>
    private static string BuildFullyTypedNewSource(NewDisambiguationScenario scenario)
    {
        var args = string.Join(", ", scenario.Arguments);
        return $"class Test {{ void Run() {{ var x = new {scenario.TypeName}({args}); }} }}";
    }

    /// <summary>
    /// Builds source for a target-typed new expression: <c>new(args)</c>.
    /// </summary>
    private static string BuildTargetTypedNewSource(NewDisambiguationScenario scenario)
    {
        var args = string.Join(", ", scenario.Arguments);
        return $"class Test {{ void Run() {{ {scenario.TypeName} x = new({args}); }} }}";
    }

    /// <summary>
    /// Builds source for a target-typed new with no args and property initializer: <c>new {{ ... }}</c>.
    /// </summary>
    private static string BuildTargetTypedNewNoArgsSource(NewDisambiguationScenario scenario)
    {
        return $"class Test {{ void Run() {{ {scenario.TypeName} x = new {{ Name = 1 }}; }} }}";
    }

    /// <summary>
    /// Extracts the primary node from the first variable declaration initializer.
    /// </summary>
    private static PrimaryNode? ExtractPrimaryFromFirstVar(CompilationUnitNode cu)
    {
        var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
        if (typeDecl is null) return null;

        var methodMember = typeDecl.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode;
        if (methodMember is null) return null;

        var block = methodMember.Method.Body.GetBlock();
        if (block is null) return null;

        if (block.Statements.Length < 1) return null;

        var stmt = block.Statements[0] as VariableDeclarationStatementStatementNode;
        if (stmt is null) return null;

        ExpressionNode? initializer = null;

        if (stmt.VariableDeclaration is InferredVariableDeclarationVariableDeclarationStatementNode inferred)
        {
            initializer = inferred.Initializer;
        }
        else if (stmt.VariableDeclaration is TypedVariableDeclarationVariableDeclarationStatementNode typed)
        {
            initializer = typed.Initializer;
        }

        if (initializer is null) return null;

        return UnwrapToPrimary(initializer);
    }

    /// <summary>
    /// Unwraps an expression down to its primary node.
    /// </summary>
    private static PrimaryNode? UnwrapToPrimary(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PrimaryNode primary)
                return primary;

            Storm.Runtime.SyntaxNode? next = null;
            foreach (var child in current.Children())
            {
                if (child is null) continue;
                if (child is PrimaryNode childPrimary)
                    return childPrimary;
                next ??= child;
            }

            if (next is null)
                return null;

            current = next;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2a: new TypeName(args) always produces ObjectCreationExpressionPrimaryNode
    //
    // For any valid type name and argument list, parsing `new TypeName(args)`
    // SHALL always produce an ObjectCreationExpressionPrimaryNode, never a
    // TargetTypedNewExpressionPrimaryNode.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TargetTypedNewDisambiguationPropertyTests) })]
    public Property FullyTypedNew_AlwaysProducesObjectCreationExpression(NewDisambiguationScenario scenario)
    {
        var source = BuildFullyTypedNewSource(scenario);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var primary = ExtractPrimaryFromFirstVar(root);
        if (primary is null)
            return false.ToProperty().Label($"Could not extract primary node from: {source}");

        var isObjectCreation = primary is ObjectCreationExpressionPrimaryNode;

        return isObjectCreation
            .ToProperty()
            .Label($"Expected ObjectCreationExpressionPrimaryNode but got {primary.GetType().Name} for: {source}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2b: new(args) always produces TargetTypedNewExpressionPrimaryNode
    //
    // For any argument list, parsing `new(args)` (with no type name between
    // `new` and `(`) SHALL always produce a TargetTypedNewExpressionPrimaryNode.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TargetTypedNewDisambiguationPropertyTests) })]
    public Property TargetTypedNew_AlwaysProducesTargetTypedNewExpression(NewDisambiguationScenario scenario)
    {
        var source = BuildTargetTypedNewSource(scenario);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var primary = ExtractPrimaryFromFirstVar(root);
        if (primary is null)
            return false.ToProperty().Label($"Could not extract primary node from: {source}");

        var isTargetTypedNew = primary is TargetTypedNewExpressionPrimaryNode;

        return isTargetTypedNew
            .ToProperty()
            .Label($"Expected TargetTypedNewExpressionPrimaryNode but got {primary.GetType().Name} for: {source}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 2c: new { Name = expr } now produces AnonymousTypeExpressionPrimaryNode
    //
    // After anonymous types were added to the grammar (ordered before
    // TargetTypedNewExpressionNoArgs), parsing `new { Name = 1 }` SHALL
    // produce an AnonymousTypeExpressionPrimaryNode because the content
    // matches AnonymousPropertyList.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TargetTypedNewDisambiguationPropertyTests) })]
    public Property NewWithPropertyInitializer_ProducesAnonymousTypeExpression(NewDisambiguationScenario scenario)
    {
        var source = BuildTargetTypedNewNoArgsSource(scenario);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var primary = ExtractPrimaryFromFirstVar(root);
        if (primary is null)
            return false.ToProperty().Label($"Could not extract primary node from: {source}");

        var isAnonymousType = primary is AnonymousTypeExpressionPrimaryNode;

        return isAnonymousType
            .ToProperty()
            .Label($"Expected AnonymousTypeExpressionPrimaryNode but got {primary.GetType().Name} for: {source}");
    }
}
