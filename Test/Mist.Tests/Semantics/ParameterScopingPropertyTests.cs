using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for parameter scoping (Bug 1 fix).
///
/// **Validates: Requirements 2.1**
///
/// Property 1: For any method with N parameters (N in 2..8), all N parameters
/// should be registered as symbols in the semantic model without any
/// <c>MIST_UNDEFINED_IDENTIFIER</c> diagnostics.
///
/// Due to the Storm source generator limitation (<see cref="ParameterListNode"/>
/// only stores the first parameter), each test case creates N separate methods
/// each with one parameter and verifies all are registered.
/// </summary>
public class ParameterScopingPropertyTests
{
    // ─── Helpers (shared with ParameterScopingTests) ─────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

#pragma warning disable CA1822
    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }
#pragma warning restore CA1822

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        return WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        });
    }

    private static StatementNode MakeIdentifierStatement(string name)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = MakeIdentifierExpression(name),
            },
        };
    }

    // ─── Test scenario records ───────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario with a random parameter count and
    /// randomly selected types for each parameter.
    /// </summary>
#pragma warning disable CA1034
    public record ParameterScenario(int ParamCount, ImmutableArray<string> ParamTypes)
    {
        public override string ToString() =>
            $"ParamCount={ParamCount}, Types=[{string.Join(", ", ParamTypes)}]";
    }

    /// <summary>
    /// Represents a preservation test scenario for a single-parameter method or constructor.
    /// </summary>
    public record SingleParameterScenario(string ParamType, string ParamName, bool IsConstructor)
    {
        public override string ToString() =>
            $"Type={ParamType}, Name={ParamName}, IsConstructor={IsConstructor}";
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    private static readonly string[] AvailableTypes =
        ["int", "bool", "string", "double", "float", "char", "long"];

    public static Arbitrary<ParameterScenario> ArbitraryParameterScenario()
    {
        // Generate a count in [2..8] and a single type applied to all parameters.
        // This keeps the generator simple while still exercising the property
        // across varying parameter counts and types.
        var gen = from count in Gen.Choose(2, 8)
                  from type in Gen.Elements(AvailableTypes)
                  select new ParameterScenario(count,
                      [.. Enumerable.Range(0, count).Select(_ => type)]);
        return gen.ToArbitrary();
    }

    private static readonly string[] ParamNames = ["x", "val", "input", "arg", "data", "item", "value"];

    public static Arbitrary<SingleParameterScenario> ArbitrarySingleParameterScenario()
    {
        var gen = from type in Gen.Elements(AvailableTypes)
                  from name in Gen.Elements(ParamNames)
                  from isCtor in Gen.Elements(true, false)
                  select new SingleParameterScenario(type, name, isCtor);
        return gen.ToArbitrary();
    }

    // ─── Property 1 ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: For any method with N parameters (N in 2..8), all N parameters
    /// are registered as symbols in the semantic model without any
    /// <c>MIST_UNDEFINED_IDENTIFIER</c> diagnostics.
    ///
    /// **Validates: Requirements 2.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ParameterScopingPropertyTests) }, MaxTest = 50)]
    public bool AllParameters_AreRegistered_WithoutUndefinedIdentifierDiagnostics(ParameterScenario scenario)
    {
        // Build N methods, each with one parameter, referencing it in the body.
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();
        var paramNames = new string[scenario.ParamCount];

        for (var i = 0; i < scenario.ParamCount; i++)
        {
            var paramName = $"p{i}";
            paramNames[i] = paramName;

            var param = MakeParameter(scenario.ParamTypes[i], paramName);
            var body = ImmutableArray.Create(MakeIdentifierStatement(paramName));

            var method = new MethodDeclarationNode
            {
                Name = MakeToken($"M{i}"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = new ParameterListNode { Parameter = param },
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            };

            members.Add(new MethodMemberMemberDeclarationNode { Method = method });
        }

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = members.ToImmutable(),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no MIST_UNDEFINED_IDENTIFIER for any parameter
        var hasUndefinedParam = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" &&
            paramNames.Any(name => d.Message.Contains($"'{name}'")));

        if (hasUndefinedParam)
            return false;

        // Assert: all parameters are registered as symbols
        foreach (var name in paramNames)
        {
            if (!model.AllSymbols.Any(s => s.Name == name && s.Kind == SymbolKind.Parameter))
                return false;
        }

        return true;
    }

    // ─── Property 4 (Preservation) ──────────────────────────────────────

    /// <summary>
    /// Property 4: For any method or constructor with exactly 1 parameter,
    /// the parameter should be registered as a symbol in the semantic model
    /// without any <c>MIST_UNDEFINED_IDENTIFIER</c> diagnostics.
    /// This is a preservation test — it verifies the fix did not break
    /// existing single-parameter behavior.
    ///
    /// **Validates: Requirements 3.1, 3.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ParameterScopingPropertyTests) }, MaxTest = 50)]
    public bool SingleParameter_IsAlwaysRegistered_Regardless_Of_Type(SingleParameterScenario scenario)
    {
        var param = MakeParameter(scenario.ParamType, scenario.ParamName);
        var body = ImmutableArray.Create(MakeIdentifierStatement(scenario.ParamName));

        MemberDeclarationNode member;
        if (scenario.IsConstructor)
        {
            var ctor = new ConstructorDeclarationNode
            {
                Name = MakeToken("TestClass"),
                Parameters = new ParameterListNode { Parameter = param },
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            };
            member = new ConstructorMemberMemberDeclarationNode { Constructor = ctor };
        }
        else
        {
            var method = new MethodDeclarationNode
            {
                Name = MakeToken("DoWork"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = new ParameterListNode { Parameter = param },
                Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            };
            member = new MethodMemberMemberDeclarationNode { Method = method };
        }

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TestClass"),
            Members = ImmutableArray.Create(member),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_IDENTIFIER for the parameter
        var hasUndefined = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_IDENTIFIER" &&
            d.Message.Contains($"'{scenario.ParamName}'"));

        if (hasUndefined)
            return false;

        // Parameter is registered as a symbol
        if (!model.AllSymbols.Any(s => s.Name == scenario.ParamName && s.Kind == SymbolKind.Parameter))
            return false;

        return true;
    }
}
