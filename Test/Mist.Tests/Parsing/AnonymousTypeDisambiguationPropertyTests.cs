// Feature: anonymous-types, Property 7: Disambiguation
// Feature: anonymous-types, Property 8: Non-anonymous fallthrough
// **Validates: Requirements 6.4, 6.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a source string containing an anonymous type expression that should
/// parse as <see cref="AnonymousTypeExpressionPrimaryNode"/>.
/// </summary>
public sealed class AnonymousContentSource
{
    public string Source { get; }
    public string Description { get; }

    public AnonymousContentSource(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => $"{Description}: {Source}";
}

/// <summary>
/// Represents a source string containing a target-typed new expression with empty
/// braces that should parse as <see cref="TargetTypedNewExpressionNoArgsPrimaryNode"/>.
/// </summary>
public sealed class NonAnonymousContentSource
{
    public string Source { get; }
    public string Description { get; }

    public NonAnonymousContentSource(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => $"{Description}: {Source}";
}

/// <summary>
/// Property-based tests for grammar disambiguation between anonymous type expressions
/// and target-typed new expressions with no arguments.
///
/// Property 7: For any source of the form <c>new { content }</c> where content matches
/// one or more <c>IDENTIFIER EQUAL Expression</c> or valid projection forms, the parser
/// SHALL produce an <see cref="AnonymousTypeExpressionPrimaryNode"/>.
///
/// Property 8: For any source of the form <c>new { content }</c> where content does NOT
/// match anonymous property syntax (empty braces), the parser SHALL produce a
/// <see cref="TargetTypedNewExpressionNoArgsPrimaryNode"/>.
/// </summary>
public class AnonymousTypeDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

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

        if(errors.Count > 0)
        {
            return null;
        }

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Extracts the primary node from the first variable declaration initializer.
    /// </summary>
    private static PrimaryNode? ExtractPrimaryFromFirstVar(CompilationUnitNode cu)
    {
        var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
        if(typeDecl is null)
        {
            return null;
        }

        var methodMember = typeDecl.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode;
        if(methodMember is null)
        {
            return null;
        }

        var block = methodMember.Method.Body.GetBlock();
        if(block is null)
        {
            return null;
        }

        if(block.Statements.Length < 1)
        {
            return null;
        }

        var stmt = block.Statements[0] as VariableDeclarationStatementStatementNode;
        if(stmt is null)
        {
            return null;
        }

        ExpressionNode? initializer = null;

        if(stmt.VariableDeclaration is InferredVariableDeclarationVariableDeclarationStatementNode inferred)
        {
            initializer = inferred.Initializer;
        }
        else if(stmt.VariableDeclaration is TypedVariableDeclarationVariableDeclarationStatementNode typed)
        {
            initializer = typed.Initializer;
        }

        if(initializer is null)
        {
            return null;
        }

        return UnwrapToPrimary(initializer);
    }

    /// <summary>
    /// Unwraps an expression down to its primary node.
    /// </summary>
    private static PrimaryNode? UnwrapToPrimary(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PrimaryNode primary)
            {
                return primary;
            }

            Storm.Runtime.SyntaxNode? next = null;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                if(child is PrimaryNode childPrimary)
                {
                    return childPrimary;
                }

                next ??= child;
            }

            if(next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 7: Disambiguation — anonymous content produces
    //             AnonymousTypeExpressionPrimaryNode
    //
    // For any source of the form `new { content }` where content matches one
    // or more `IDENTIFIER EQUAL Expression` or valid projection forms, the
    // parser SHALL produce an AnonymousTypeExpressionPrimaryNode.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AnonymousTypeDisambiguationGenerator) })]
    public Property AnonymousContent_ProducesAnonymousTypeExpression(AnonymousContentSource scenario)
    {
        var root = ParseSource(scenario.Source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {scenario.Source}");
        }

        var primary = ExtractPrimaryFromFirstVar(root);
        if(primary is null)
        {
            return false.ToProperty().Label($"Could not extract primary node from: {scenario.Source}");
        }

        var isAnonymousType = primary is AnonymousTypeExpressionPrimaryNode;

        return isAnonymousType
            .ToProperty()
            .Label($"Expected AnonymousTypeExpressionPrimaryNode but got {primary.GetType().Name} for: {scenario}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 8: Non-anonymous content falls through to target-typed new
    //
    // For any source of the form `new { }` (empty braces — no content matching
    // anonymous property syntax), the parser SHALL produce a
    // TargetTypedNewExpressionNoArgsPrimaryNode.
    //
    // NOTE: With the current grammar, AnonymousPropertyList requires at least
    // one property. Empty braces are the only case at the grammar level that
    // falls through to TargetTypedNewExpressionNoArgs.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AnonymousTypeDisambiguationGenerator) })]
    public Property NonAnonymousContent_ProducesTargetTypedNewExpressionNoArgs(NonAnonymousContentSource scenario)
    {
        var root = ParseSource(scenario.Source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {scenario.Source}");
        }

        var primary = ExtractPrimaryFromFirstVar(root);
        if(primary is null)
        {
            return false.ToProperty().Label($"Could not extract primary node from: {scenario.Source}");
        }

        var isTargetTypedNewNoArgs = primary is TargetTypedNewExpressionNoArgsPrimaryNode;

        return isTargetTypedNewNoArgs
            .ToProperty()
            .Label($"Expected TargetTypedNewExpressionNoArgsPrimaryNode but got {primary.GetType().Name} for: {scenario}");
    }
}

// ─── Generator ──────────────────────────────────────────────────────────────

/// <summary>
/// Generates source strings for disambiguation property tests.
/// </summary>
public static class AnonymousTypeDisambiguationGenerator
{
    private static readonly string[] Identifiers =
        ["Name", "Value", "Count", "Label", "Tag", "X", "Y", "Id", "Key", "Score"];

    private static readonly string[] SimpleExpressions =
        ["42", "1", "0", "100", "true", "false", "\"hello\"", "\"\""];

    private static readonly string[] IdentifierExpressions =
        ["x", "y", "z", "value", "name", "count", "item", "result"];

    private static readonly string[] MemberAccessExpressions =
        ["obj.Name", "item.Value", "self.Count", "data.Key", "node.Label"];

    private static readonly string[] TypeNames =
        ["Config", "Settings", "Options", "Point", "Data"];

    private static string WrapInMethod(string expression)
    {
        return $"class Test {{ void Run() {{ var x = {expression}; }} }}";
    }

    private static string WrapInTypedMethod(string typeName, string expression)
    {
        return $"class Test {{ void Run() {{ {typeName} x = {expression}; }} }}";
    }

    // ─── Anonymous content generators (Property 7) ──────────────────────

    /// <summary>
    /// Generates explicit assignment forms: <c>new { Name = expr }</c>
    /// </summary>
    private static Gen<AnonymousContentSource> GenExplicitAssignment()
    {
        return from propCount in Gen.Choose(1, 3)
               from nameIndices in Gen.ArrayOf(Gen.Choose(0, Identifiers.Length - 1), propCount)
               from exprIndices in Gen.ArrayOf(Gen.Choose(0, SimpleExpressions.Length - 1), propCount)
               let props = Enumerable.Range(0, propCount)
                   .Select(i => $"{Identifiers[nameIndices[i]]} = {SimpleExpressions[exprIndices[i]]}")
                   .ToArray()
               let content = string.Join(", ", props)
               select new AnonymousContentSource(
                   WrapInMethod($"new {{ {content} }}"),
                   $"Explicit: new {{ {content} }}");
    }

    /// <summary>
    /// Generates simple identifier projection forms: <c>new { variable }</c>
    /// </summary>
    private static Gen<AnonymousContentSource> GenIdentifierProjection()
    {
        return from count in Gen.Choose(1, 3)
               from indices in Gen.ArrayOf(Gen.Choose(0, IdentifierExpressions.Length - 1), count)
               let vars = indices.Select(i => IdentifierExpressions[i]).Distinct().ToArray()
               where vars.Length >= 1
               let content = string.Join(", ", vars)
               select new AnonymousContentSource(
                   WrapInMethod($"new {{ {content} }}"),
                   $"Projection: new {{ {content} }}");
    }

    /// <summary>
    /// Generates member access projection forms: <c>new { obj.Member }</c>
    /// </summary>
    private static Gen<AnonymousContentSource> GenMemberAccessProjection()
    {
        return from count in Gen.Choose(1, 2)
               from indices in Gen.ArrayOf(Gen.Choose(0, MemberAccessExpressions.Length - 1), count)
               let members = indices.Select(i => MemberAccessExpressions[i]).Distinct().ToArray()
               where members.Length >= 1
               let content = string.Join(", ", members)
               select new AnonymousContentSource(
                   WrapInMethod($"new {{ {content} }}"),
                   $"MemberProjection: new {{ {content} }}");
    }

    /// <summary>
    /// Generates mixed forms combining explicit assignments and projections:
    /// <c>new { Name = expr, variable }</c>
    /// </summary>
    private static Gen<AnonymousContentSource> GenMixedForm()
    {
        return from nameIdx in Gen.Choose(0, Identifiers.Length - 1)
               from exprIdx in Gen.Choose(0, SimpleExpressions.Length - 1)
               from varIdx in Gen.Choose(0, IdentifierExpressions.Length - 1)
               let explicit_ = $"{Identifiers[nameIdx]} = {SimpleExpressions[exprIdx]}"
               let projected = IdentifierExpressions[varIdx]
               let content = $"{explicit_}, {projected}"
               select new AnonymousContentSource(
                   WrapInMethod($"new {{ {content} }}"),
                   $"Mixed: new {{ {content} }}");
    }

    /// <summary>
    /// Generates explicit assignment with trailing comma: <c>new { Name = expr, }</c>
    /// </summary>
    private static Gen<AnonymousContentSource> GenTrailingComma()
    {
        return from nameIdx in Gen.Choose(0, Identifiers.Length - 1)
               from exprIdx in Gen.Choose(0, SimpleExpressions.Length - 1)
               let content = $"{Identifiers[nameIdx]} = {SimpleExpressions[exprIdx]},"
               select new AnonymousContentSource(
                   WrapInMethod($"new {{ {content} }}"),
                   $"TrailingComma: new {{ {content} }}");
    }

    // ─── Non-anonymous content generators (Property 8) ──────────────────

    /// <summary>
    /// Generates empty brace forms: <c>new { }</c>
    /// These fall through to TargetTypedNewExpressionNoArgs because
    /// AnonymousPropertyList requires at least one property.
    /// </summary>
    private static Gen<NonAnonymousContentSource> GenEmptyBraces()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               select new NonAnonymousContentSource(
                   WrapInTypedMethod(TypeNames[typeIdx], "new { }"),
                   $"EmptyBraces: {TypeNames[typeIdx]} x = new {{ }}");
    }

    // ─── Arbitrary instances ────────────────────────────────────────────

    public static Arbitrary<AnonymousContentSource> ArbitraryAnonymousContentSource()
    {
        var gen = Gen.Frequency(
            (4, GenExplicitAssignment()),
            (3, GenIdentifierProjection()),
            (2, GenMemberAccessProjection()),
            (2, GenMixedForm()),
            (1, GenTrailingComma()));

        return gen.ToArbitrary();
    }

    public static Arbitrary<NonAnonymousContentSource> ArbitraryNonAnonymousContentSource()
    {
        return GenEmptyBraces().ToArbitrary();
    }
}