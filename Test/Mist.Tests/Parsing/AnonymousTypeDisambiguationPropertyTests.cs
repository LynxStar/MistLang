// Feature: anonymous-types, Property 7: Disambiguation
// Feature: anonymous-types, Property 8: Non-anonymous fallthrough
// **Validates: Requirements 6.4, 6.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

public sealed class AnonymousContentSource
{
    public string Source { get; }
    public string Description { get; }

    public AnonymousContentSource(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return $"{Description}: {Source}";
    }
}

public sealed class NonAnonymousContentSource
{
    public string Source { get; }
    public string Description { get; }

    public NonAnonymousContentSource(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return $"{Description}: {Source}";
    }
}

public class AnonymousTypeDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
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

    private static PrimaryNode? ExtractPrimaryFromFirstVar(CompilationUnitNode cu)
    {
        if(cu.GetTypes()[0] is not BodyTypeTypeDeclarationNode typeDecl)
        {
            return null;
        }

        if(typeDecl.TypeBody.Members[0] is not MethodMemberMemberDeclarationNode methodMember)
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

        if(block.Statements[0] is not VariableDeclarationStatementStatementNode stmt)
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

    private static Gen<AnonymousContentSource> GenTrailingComma()
    {
        return from nameIdx in Gen.Choose(0, Identifiers.Length - 1)
               from exprIdx in Gen.Choose(0, SimpleExpressions.Length - 1)
               let content = $"{Identifiers[nameIdx]} = {SimpleExpressions[exprIdx]},"
               select new AnonymousContentSource(
                   WrapInMethod($"new {{ {content} }}"),
                   $"TrailingComma: new {{ {content} }}");
    }

    // AnonymousPropertyList requires at least one property, so empty braces are the only
    // `new { }` form that falls through to TargetTypedNewExpressionNoArgs.
    private static Gen<NonAnonymousContentSource> GenEmptyBraces()
    {
        return from typeIdx in Gen.Choose(0, TypeNames.Length - 1)
               select new NonAnonymousContentSource(
                   WrapInTypedMethod(TypeNames[typeIdx], "new { }"),
                   $"EmptyBraces: {TypeNames[typeIdx]} x = new {{ }}");
    }

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