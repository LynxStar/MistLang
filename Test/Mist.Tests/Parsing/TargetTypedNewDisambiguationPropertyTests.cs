// Feature: target-typed-new, Property 2: Parsing disambiguation
// **Validates: Requirements 1.4, 1.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

public sealed class NewDisambiguationScenario
{
    public string TypeName { get; }
    public IReadOnlyList<string> Arguments { get; }

    public NewDisambiguationScenario(string typeName, IReadOnlyList<string> arguments)
    {
        TypeName = typeName;
        Arguments = arguments;
    }

    public override string ToString()
    {
        return $"TypeName={TypeName}, Args=[{string.Join(", ", Arguments)}]";
    }
}

public class TargetTypedNewDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

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

    private static string BuildFullyTypedNewSource(NewDisambiguationScenario scenario)
    {
        var args = string.Join(", ", scenario.Arguments);
        return $"class Test {{ void Run() {{ var x = new {scenario.TypeName}({args}); }} }}";
    }

    private static string BuildTargetTypedNewSource(NewDisambiguationScenario scenario)
    {
        var args = string.Join(", ", scenario.Arguments);
        return $"class Test {{ void Run() {{ {scenario.TypeName} x = new({args}); }} }}";
    }

    private static string BuildTargetTypedNewNoArgsSource(NewDisambiguationScenario scenario)
    {
        return $"class Test {{ void Run() {{ {scenario.TypeName} x = new {{ Name = 1 }}; }} }}";
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
        Arbitrary = new[] { typeof(TargetTypedNewDisambiguationPropertyTests) })]
    public Property FullyTypedNew_AlwaysProducesObjectCreationExpression(NewDisambiguationScenario scenario)
    {
        var source = BuildFullyTypedNewSource(scenario);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var primary = ExtractPrimaryFromFirstVar(root);
        if(primary is null)
        {
            return false.ToProperty().Label($"Could not extract primary node from: {source}");
        }

        var isObjectCreation = primary is ObjectCreationExpressionPrimaryNode;

        return isObjectCreation
            .ToProperty()
            .Label($"Expected ObjectCreationExpressionPrimaryNode but got {primary.GetType().Name} for: {source}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TargetTypedNewDisambiguationPropertyTests) })]
    public Property TargetTypedNew_AlwaysProducesTargetTypedNewExpression(NewDisambiguationScenario scenario)
    {
        var source = BuildTargetTypedNewSource(scenario);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var primary = ExtractPrimaryFromFirstVar(root);
        if(primary is null)
        {
            return false.ToProperty().Label($"Could not extract primary node from: {source}");
        }

        var isTargetTypedNew = primary is TargetTypedNewExpressionPrimaryNode;

        return isTargetTypedNew
            .ToProperty()
            .Label($"Expected TargetTypedNewExpressionPrimaryNode but got {primary.GetType().Name} for: {source}");
    }

    // The grammar orders AnonymousTypeExpression before TargetTypedNewExpressionNoArgs, so
    // `new { Name = 1 }` matches AnonymousPropertyList and is an anonymous type.
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TargetTypedNewDisambiguationPropertyTests) })]
    public Property NewWithPropertyInitializer_ProducesAnonymousTypeExpression(NewDisambiguationScenario scenario)
    {
        var source = BuildTargetTypedNewNoArgsSource(scenario);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var primary = ExtractPrimaryFromFirstVar(root);
        if(primary is null)
        {
            return false.ToProperty().Label($"Could not extract primary node from: {source}");
        }

        var isAnonymousType = primary is AnonymousTypeExpressionPrimaryNode;

        return isAnonymousType
            .ToProperty()
            .Label($"Expected AnonymousTypeExpressionPrimaryNode but got {primary.GetType().Name} for: {source}");
    }
}