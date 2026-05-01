// Feature: void-unit-type, Property 7: Context-sensitive void C# emission
// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying context-sensitive void C# emission.
///
/// Property 7: For any Mist program using void as a value type (void variables,
/// void parameters, void generic arguments, or void method return values that are
/// consumed), the emitter SHALL produce C# code using the MistVoid struct type.
/// For any void method whose return value is never consumed, the emitter SHALL
/// produce standard C# void.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
/// </summary>
public sealed class VoidEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public VoidEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistVoidEmissionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
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
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates the expression <c>Void.Void</c> as an AST node.
    /// </summary>
    private static ExpressionNode MakeVoidDotVoidExpression()
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok("Void") };
        var suffix = new MemberAccessSuffixPostfixSuffixNode { Name = Tok("Void") };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        return WrapPrimaryAsExpression(primary);
    }

    /// <summary>
    /// Creates a method call expression: <c>methodName(args...)</c>
    /// </summary>
    private static ExpressionNode MakeMethodCallExpression(string methodName, ExpressionNode[]? args = null)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(methodName) };
        ArgumentListNode? argList = null;
        if (args is { Length: > 0 })
        {
            argList = new ArgumentListNode { Expression = args[0] };
        }
        var suffix = new InvocationSuffixPostfixSuffixNode { Arguments = argList };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeTypedVarDeclStatement(
        TypeReferenceNode typeRef, string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = Tok(varName),
                Initializer = initializer,
            },
        };
    }

    private static StatementNode MakeInferredVarDeclStatement(string varName, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok(varName),
                Initializer = initializer,
            },
        };
    }

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        // Return the content of the main source file (not MistVoid.g.cs or .csproj)
        var csFile = Directory.GetFiles(_outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs", StringComparison.Ordinal));
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private bool MistVoidFileExists()
    {
        var mistVoidPath = Path.Combine(_outputDir, "MistVoid.g.cs");
        return File.Exists(mistVoidPath);
    }

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    private static ClassDeclarationNode MakeProgramClass(ImmutableArray<MemberDeclarationNode> members)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var allMembers = members.Add(
            new MethodMemberMemberDeclarationNode { Method = mainMethod });

        return new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = allMembers,
        };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Whether the void method's return value is consumed (value context)
    /// or discarded (fire-and-forget context).
    /// </summary>
    public enum VoidUsageContext
    {
        /// <summary>
        /// Void method return value is consumed: assigned to a variable.
        /// Emitter should produce MistVoid return type. (Req 10.3)
        /// </summary>
        ValueConsumed,

        /// <summary>
        /// Void method called as standalone expression statement.
        /// Emitter should produce standard C# void. (Req 10.4)
        /// </summary>
        FireAndForget,
    }

    /// <summary>
    /// A scenario combining a void usage context with random method/variable names.
    /// </summary>
    public record VoidEmissionScenario(
        VoidUsageContext Context,
        string MethodName,
        string VariableName)
    {
        public override string ToString() =>
            Context == VoidUsageContext.ValueConsumed
                ? $"var {VariableName} = {MethodName}(); // value consumed → MistVoid"
                : $"{MethodName}(); // fire-and-forget → void";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Handle", "Init", "Cleanup", "Reset", "Notify", "Update"];

    private static readonly string[] VariableNamePool =
        ["result", "val", "output", "ret", "tmp", "data", "item", "value", "res", "out1"];

    public static Gen<VoidEmissionScenario> GenScenario()
    {
        return from context in Gen.Elements(
                   VoidUsageContext.ValueConsumed,
                   VoidUsageContext.FireAndForget)
               from methodName in Gen.Elements(MethodNamePool)
               from varName in Gen.Elements(VariableNamePool)
               select new VoidEmissionScenario(context, methodName, varName);
    }

    public static Arbitrary<VoidEmissionScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a program with a void method and a Main method that either
    /// consumes or discards the void method's return value.
    /// </summary>
    private static (CompilationUnitNode Ast, string Source) BuildScenario(VoidEmissionScenario scenario)
    {
        // The void method: public void MethodName() { return; }
        var voidMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.MethodName),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = null },
                    }),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Build the call site in Main
        ImmutableArray<StatementNode> mainStatements;
        string source;

        if (scenario.Context == VoidUsageContext.ValueConsumed)
        {
            // var varName = MethodName(); — consumes the return value
            var callExpr = MakeMethodCallExpression(scenario.MethodName);
            var varDecl = MakeInferredVarDeclStatement(scenario.VariableName, callExpr);
            mainStatements = ImmutableArray.Create(varDecl);
            source = $"class Program {{ public void {scenario.MethodName}() {{ return; }} public static void Main() {{ var {scenario.VariableName} = {scenario.MethodName}(); }} }}";
        }
        else
        {
            // MethodName(); — fire-and-forget
            var callExpr = MakeMethodCallExpression(scenario.MethodName);
            var callStmt = MakeExpressionStatement(callExpr);
            mainStatements = ImmutableArray.Create(callStmt);
            source = $"class Program {{ public void {scenario.MethodName}() {{ return; }} public static void Main() {{ {scenario.MethodName}(); }} }}";
        }

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = voidMethod },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return (ast, source);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 7: Context-sensitive void C# emission
    //
    // **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    //
    // For any Mist program using void as a value type (void variables,
    // void parameters, void generic arguments, or void method return
    // values that are consumed), the emitter SHALL produce C# code using
    // the MistVoid struct type. For any void method whose return value
    // is never consumed, the emitter SHALL produce standard C# void.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sub-property 7a: When a void method's return value is consumed
    /// (assigned to a variable), the emitter produces MistVoid return type
    /// and the MistVoid struct is emitted.
    ///
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(VoidEmissionPropertyTests) })]
    public bool ValueConsumedVoidMethod_EmitsMistVoid(VoidEmissionScenario scenario)
    {
        if (scenario.Context != VoidUsageContext.ValueConsumed)
            return true; // skip non-matching scenarios

        CleanOutputDir();

        var (ast, source) = BuildScenario(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Req 10.3: The void method whose return value is consumed should have MistVoid return type
        var methodSignaturePattern = new Regex(
            @"public\s+MistVoid\s+" + Regex.Escape(scenario.MethodName) + @"\s*\(");
        var hasMistVoidReturnType = methodSignaturePattern.IsMatch(csContent);

        // Req 10.1: The MistVoid struct should be emitted (in a separate shared file)
        var hasMistVoidStruct = MistVoidFileExists();

        // Req 10.5: The variable declaration should use MistVoid or var (both valid)
        // The emitter uses var for inferred declarations, which is fine
        var hasVarDecl = csContent.Contains($"var {scenario.VariableName}");

        return hasMistVoidReturnType && hasMistVoidStruct && hasVarDecl;
    }

    /// <summary>
    /// Sub-property 7b: When a void method's return value is never consumed
    /// (fire-and-forget), the emitter produces standard C# void.
    ///
    /// **Validates: Requirements 10.4, 10.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(VoidEmissionPropertyTests) })]
    public bool FireAndForgetVoidMethod_EmitsCSharpVoid(VoidEmissionScenario scenario)
    {
        if (scenario.Context != VoidUsageContext.FireAndForget)
            return true; // skip non-matching scenarios

        CleanOutputDir();

        var (ast, source) = BuildScenario(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        // Req 10.4: The void method should have standard C# void return type
        var methodSignaturePattern = new Regex(
            @"public\s+void\s+" + Regex.Escape(scenario.MethodName) + @"\s*\(");
        var hasVoidReturnType = methodSignaturePattern.IsMatch(csContent);

        // The method should NOT have MistVoid return type
        var mistVoidPattern = new Regex(
            @"public\s+MistVoid\s+" + Regex.Escape(scenario.MethodName) + @"\s*\(");
        var doesNotHaveMistVoid = !mistVoidPattern.IsMatch(csContent);

        return hasVoidReturnType && doesNotHaveMistVoid;
    }
}
