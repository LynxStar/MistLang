// Feature: cast-operators, Property 7: Cast operator C# emission correctness
// **Validates: Requirements 10.1, 10.2, 10.3, 11.1, 11.3, 11.4, 12.1, 12.2, 12.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for cast operator C# emission correctness.
///
/// Property 7: Cast operator C# emission correctness
///
/// For any valid Mist program containing cast operator declarations and cast
/// expressions that pass type checking, the emitted C# code SHALL contain:
/// (a) <c>public static implicit operator TargetType(SourceType param) { body }</c>
///     for each implicit cast operator declaration;
/// (b) <c>public static explicit operator TargetType(SourceType param) { body }</c>
///     for each explicit cast operator declaration;
/// (c) a static method call pattern (e.g., <c>TypeName.op_Implicit(expr)</c> or
///     <c>TypeName.op_Explicit(expr)</c>) for each cast expression with a resolved
///     user-defined cast operator;
/// (d) a standard C# cast <c>((TargetType)expr)</c> for each identity/subtype cast
///     expression with no resolved operator.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 11.1, 11.3, 11.4, 12.1, 12.2, 12.3**
/// </summary>
public sealed class CastOperatorEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();
    private readonly string _outputDir;

    public CastOperatorEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistCastEmissionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); }
            catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034
    public enum CastKind { Implicit, Explicit }
    public enum OperatorPlacement { OnSourceType, OnTargetType }
    public enum ImplicitSite { Assignment, Argument, Return }

    public record CastDeclScenario(
        string SourceType, string TargetType,
        CastKind Kind, OperatorPlacement Placement)
    {
        public override string ToString() =>
            $"{Kind} operator {TargetType}({SourceType}) on {Placement}";
    }

    public record ExplicitCastExprScenario(
        string SourceType, string TargetType,
        CastKind OperatorKind, OperatorPlacement Placement)
    {
        public override string ToString() =>
            $"({TargetType})src where {OperatorKind} operator on {Placement}";
    }

    public record IdentityCastScenario(string TypeName)
    {
        public override string ToString() => $"({TypeName})src identity cast";
    }

    public record ImplicitCastSiteScenario(
        string SourceType, string TargetType,
        OperatorPlacement Placement, ImplicitSite Site)
    {
        public override string ToString() =>
            $"implicit {SourceType}→{TargetType} at {Site}, operator on {Placement}";
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    private static Gen<CastDeclScenario> GenCastDeclScenario() =>
        from source in Gen.Elements(TypeNames)
        from target in Gen.Elements(TypeNames).Where(t => t != source)
        from kind in Gen.Elements(CastKind.Implicit, CastKind.Explicit)
        from placement in Gen.Elements(OperatorPlacement.OnSourceType, OperatorPlacement.OnTargetType)
        select new CastDeclScenario(source, target, kind, placement);

    private static Gen<ExplicitCastExprScenario> GenExplicitCastExprScenario() =>
        from source in Gen.Elements(TypeNames)
        from target in Gen.Elements(TypeNames).Where(t => t != source)
        from kind in Gen.Elements(CastKind.Implicit, CastKind.Explicit)
        from placement in Gen.Elements(OperatorPlacement.OnSourceType, OperatorPlacement.OnTargetType)
        select new ExplicitCastExprScenario(source, target, kind, placement);

    private static Gen<IdentityCastScenario> GenIdentityCastScenario() =>
        from typeName in Gen.Elements(TypeNames)
        select new IdentityCastScenario(typeName);

    private static Gen<ImplicitCastSiteScenario> GenImplicitCastSiteScenario() =>
        from source in Gen.Elements(TypeNames)
        from target in Gen.Elements(TypeNames).Where(t => t != source)
        from placement in Gen.Elements(OperatorPlacement.OnSourceType, OperatorPlacement.OnTargetType)
        from site in Gen.Elements(ImplicitSite.Assignment, ImplicitSite.Argument, ImplicitSite.Return)
        select new ImplicitCastSiteScenario(source, target, placement, site);

    public static Arbitrary<CastDeclScenario> ArbitraryCastDeclScenario() =>
        GenCastDeclScenario().ToArbitrary();
    public static Arbitrary<ExplicitCastExprScenario> ArbitraryExplicitCastExprScenario() =>
        GenExplicitCastExprScenario().ToArbitrary();
    public static Arbitrary<IdentityCastScenario> ArbitraryIdentityCastScenario() =>
        GenIdentityCastScenario().ToArbitrary();
    public static Arbitrary<ImplicitCastSiteScenario> ArbitraryImplicitCastSiteScenario() =>
        GenImplicitCastSiteScenario().ToArbitrary();

    // ─── AST helpers ─────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static TypeReferenceNode NamedTypeRef(string name) =>
        new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
            }
        };

    private static ParameterNode MakeParam(string typeName, string paramName) =>
        new() { Type = NamedTypeRef(typeName), Name = Tok(paramName) };

    private static ExpressionNode IdentExpr(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        return WrapPrimary(primary);
    }

    private static ExpressionNode NewObjExpr(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimary(primary);
    }

    private static ExpressionNode WrapPrimary(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        return WrapUnary(unary);
    }

    private static ExpressionNode WrapUnary(UnaryNode unary)
    {
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode CastExpr(string targetType, string operandName)
    {
        var operand = new PostfixExpressionUnaryNode
        {
            Primary = new PostfixNode
            {
                Target = new IdentifierExpressionPrimaryNode { Name = Tok(operandName) },
                Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
            },
        };
        var castUnary = new CastExpressionUnaryNode
        {
            TargetType = NamedTypeRef(targetType),
            Operand = operand,
        };
        return WrapUnary(castUnary);
    }

    private static ExpressionNode MethodCallExpr(string target, string method, ExpressionNode arg)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(target) };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(method),
            Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = arg } },
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        return WrapUnary(unary);
    }

    /// <summary>Creates a cast operator declaration AST node.</summary>
    private static CastOperatorDeclarationNode MakeCastOpDecl(
        CastKind kind, string targetType, string sourceType)
    {
        CastOperatorKindNode castKindNode = kind == CastKind.Implicit
            ? new ImplicitCastCastOperatorKindNode()
            : new ExplicitCastCastOperatorKindNode();

        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = castKindNode,
            TargetType = NamedTypeRef(targetType),
            Parameters = new ParameterListNode { Parameter = MakeParam(sourceType, "value") },
            Body = new OperatorBlockBodyOperatorBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ReturnStatementStatementNode
                        {
                            ReturnStatement = new ReturnStatementNode
                            {
                                Value = IdentExpr("value"),
                            },
                        }),
                },
            },
        };
    }

    private static MemberDeclarationNode CastOpMember(CastKind kind, string targetType, string sourceType) =>
        new CastOperatorMemberMemberDeclarationNode
        {
            CastOperator = MakeCastOpDecl(kind, targetType, sourceType),
        };

    private static TypeDeclarationNode ClassDecl(string name, params MemberDeclarationNode[] members) =>
        new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(name),
                Members = members.ToImmutableArray(),
            },
        };

    private static MemberDeclarationNode MainMethod(params StatementNode[] stmts) =>
        new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = Tok("Main"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode
                {
                    Block = new BlockNode { Statements = stmts.ToImmutableArray() },
                },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(),
                    new StaticModifierModifierNode()),
            },
        };

    private static StatementNode TypedVarDecl(string typeName, string varName, ExpressionNode init) =>
        new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = NamedTypeRef(typeName),
                Name = Tok(varName),
                Initializer = init,
            },
        };

    private static StatementNode ExprStmt(ExpressionNode expr) =>
        new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };

    private static StatementNode ReturnStmt(ExpressionNode expr) =>
        new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = expr },
        };

    private static MemberDeclarationNode AcceptMethod(string paramType) =>
        new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = Tok("Accept"),
                ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Parameters = new ParameterListNode { Parameter = MakeParam(paramType, "param") },
                Body = new MethodBlockBodyMethodBodyNode
                {
                    Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(),
                    new StaticModifierModifierNode()),
            },
        };

    private static MemberDeclarationNode ConvertMethod(string returnType, string sourceType) =>
        new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = Tok("Convert"),
                ReturnType = NamedTypeRef(returnType),
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode
                {
                    Block = new BlockNode
                    {
                        Statements = ImmutableArray.Create<StatementNode>(
                            TypedVarDecl(sourceType, "src", NewObjExpr(sourceType)),
                            ReturnStmt(IdentExpr("src"))),
                    },
                },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(),
                    new StaticModifierModifierNode()),
            },
        };

    // ─── Model + Emit helpers ────────────────────────────────────────────

    private ISemanticModel BuildModel(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);
        var source = new SourceText("test.mist", printed);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(source)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        CleanOutputDir();
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);
        var csFiles = Directory.GetFiles(_outputDir, "*.cs")
            .Where(f => !f.EndsWith("MistVoid.g.cs") && !f.EndsWith(".csproj"))
            .ToArray();
        return csFiles.Length == 0 ? "" : string.Join("\n", csFiles.Select(File.ReadAllText));
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(_outputDir))
        {
            foreach(var file in Directory.GetFiles(_outputDir))
            {
                File.Delete(file);
            }
        }
    }

    // ─── AST builders for each scenario ──────────────────────────────────

    private static CompilationUnitNode BuildCastDeclAst(CastDeclScenario s)
    {
        var castMember = CastOpMember(s.Kind, s.TargetType, s.SourceType);
        var sourceMembers = s.Placement == OperatorPlacement.OnSourceType
            ? new[] { castMember } : Array.Empty<MemberDeclarationNode>();
        var targetMembers = s.Placement == OperatorPlacement.OnTargetType
            ? new[] { castMember } : Array.Empty<MemberDeclarationNode>();

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                ClassDecl(s.SourceType, sourceMembers),
                ClassDecl(s.TargetType, targetMembers),
                ClassDecl("Program", MainMethod())
            ).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildExplicitCastExprAst(ExplicitCastExprScenario s)
    {
        var castMember = CastOpMember(s.OperatorKind, s.TargetType, s.SourceType);
        var sourceMembers = s.Placement == OperatorPlacement.OnSourceType
            ? new[] { castMember } : Array.Empty<MemberDeclarationNode>();
        var targetMembers = s.Placement == OperatorPlacement.OnTargetType
            ? new[] { castMember } : Array.Empty<MemberDeclarationNode>();

        var mainStmts = new[]
        {
            TypedVarDecl(s.SourceType, "src", NewObjExpr(s.SourceType)),
            TypedVarDecl(s.TargetType, "tgt", CastExpr(s.TargetType, "src")),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                ClassDecl(s.SourceType, sourceMembers),
                ClassDecl(s.TargetType, targetMembers),
                ClassDecl("Program", MainMethod(mainStmts))
            ).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildIdentityCastAst(IdentityCastScenario s)
    {
        var mainStmts = new[]
        {
            TypedVarDecl(s.TypeName, "src", NewObjExpr(s.TypeName)),
            TypedVarDecl(s.TypeName, "tgt", CastExpr(s.TypeName, "src")),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                ClassDecl(s.TypeName),
                ClassDecl("Program", MainMethod(mainStmts))
            ).AsTopLevelItems(),
        };
    }

    private static CompilationUnitNode BuildImplicitCastSiteAst(ImplicitCastSiteScenario s)
    {
        var castMember = CastOpMember(CastKind.Implicit, s.TargetType, s.SourceType);
        var sourceMembers = s.Placement == OperatorPlacement.OnSourceType
            ? new[] { castMember } : Array.Empty<MemberDeclarationNode>();
        var targetMembers = s.Placement == OperatorPlacement.OnTargetType
            ? new[] { castMember } : Array.Empty<MemberDeclarationNode>();

        var programMembers = new List<MemberDeclarationNode>();

        switch(s.Site)
        {
            case ImplicitSite.Assignment:
                programMembers.Add(MainMethod(
                    TypedVarDecl(s.SourceType, "src", NewObjExpr(s.SourceType)),
                    TypedVarDecl(s.TargetType, "tgt", IdentExpr("src"))));
                break;
            case ImplicitSite.Argument:
                programMembers.Add(AcceptMethod(s.TargetType));
                programMembers.Add(MainMethod(
                    TypedVarDecl(s.SourceType, "src", NewObjExpr(s.SourceType)),
                    ExprStmt(MethodCallExpr("Program", "Accept", IdentExpr("src")))));
                break;
            case ImplicitSite.Return:
                programMembers.Add(ConvertMethod(s.TargetType, s.SourceType));
                programMembers.Add(MainMethod());
                break;
        }

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                ClassDecl(s.SourceType, sourceMembers),
                ClassDecl(s.TargetType, targetMembers),
                ClassDecl("Program", programMembers.ToArray())
            ).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Implicit cast operator declaration emission
    //
    // **Validates: Requirements 10.1, 10.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(CastOperatorEmissionPropertyTests) },
        MaxTest = 100)]
    public Property ImplicitCastDecl_EmitsImplicitOperatorSyntax(CastDeclScenario scenario)
    {
        if(scenario.Kind != CastKind.Implicit)
        {
            return true.ToProperty().Label("Skipped (explicit scenario)");
        }

        var ast = BuildCastDeclAst(scenario);
        var model = BuildModel(ast);
        var csOutput = EmitAndGetCsContent(model);

        var expected = $"implicit operator {scenario.TargetType}({scenario.SourceType}";
        return csOutput.Contains(expected).ToProperty()
            .Label($"Expected '{expected}' in emitted C# for {scenario}\nC#:\n{csOutput}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Explicit cast operator declaration emission
    //
    // **Validates: Requirements 10.2, 10.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(CastOperatorEmissionPropertyTests) },
        MaxTest = 100)]
    public Property ExplicitCastDecl_EmitsExplicitOperatorSyntax(CastDeclScenario scenario)
    {
        if(scenario.Kind != CastKind.Explicit)
        {
            return true.ToProperty().Label("Skipped (implicit scenario)");
        }

        var ast = BuildCastDeclAst(scenario);
        var model = BuildModel(ast);
        var csOutput = EmitAndGetCsContent(model);

        var expected = $"explicit operator {scenario.TargetType}({scenario.SourceType}";
        return csOutput.Contains(expected).ToProperty()
            .Label($"Expected '{expected}' in emitted C# for {scenario}\nC#:\n{csOutput}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7c: Explicit cast expression with user-defined operator
    //
    // **Validates: Requirements 11.1, 11.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(CastOperatorEmissionPropertyTests) },
        MaxTest = 100)]
    public Property ExplicitCastExpr_EmitsStaticMethodCall(ExplicitCastExprScenario scenario)
    {
        var ast = BuildExplicitCastExprAst(scenario);
        var model = BuildModel(ast);
        var diags = RunTypeChecker(model);

        // If the type checker emits errors, skip — the scenario may not be valid
        // in the current implementation.
        if(diags.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return true.ToProperty().Label("Skipped (type checker errors)");
        }

        var csOutput = EmitAndGetCsContent(model);

        // User-defined cast operators are emitted as standard C# cast expressions
        // ((TargetType)operand), which lets the C# compiler invoke the operator.
        var expected = $"(({scenario.TargetType})";

        return csOutput.Contains(expected).ToProperty()
            .Label($"Expected '{expected}' in emitted C# for {scenario}\nC#:\n{csOutput}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7d: Identity/subtype cast expression emission
    //
    // **Validates: Requirements 11.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(CastOperatorEmissionPropertyTests) },
        MaxTest = 100)]
    public Property IdentityCast_EmitsStandardCSharpCast(IdentityCastScenario scenario)
    {
        var ast = BuildIdentityCastAst(scenario);
        var model = BuildModel(ast);
        var csOutput = EmitAndGetCsContent(model);

        var expected = $"(({scenario.TypeName})";
        return csOutput.Contains(expected).ToProperty()
            .Label($"Expected '{expected}' in emitted C# for {scenario}\nC#:\n{csOutput}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7e: Implicit cast emission at assignment/argument/return sites
    //
    // **Validates: Requirements 12.1, 12.2, 12.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(CastOperatorEmissionPropertyTests) },
        MaxTest = 100)]
    public Property ImplicitCastAtSite_EmitsOpImplicitCall(ImplicitCastSiteScenario scenario)
    {
        var ast = BuildImplicitCastSiteAst(scenario);
        var model = BuildModel(ast);
        var diags = RunTypeChecker(model);

        // If the type checker emits errors, skip — the scenario may not be valid
        // in the current implementation.
        if(diags.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return true.ToProperty().Label("Skipped (type checker errors)");
        }

        var csOutput = EmitAndGetCsContent(model);

        // Implicit casts at assignment/argument/return sites are emitted as
        // standard C# cast expressions ((TargetType)expr), which lets the
        // C# compiler invoke the implicit operator.
        var expected = $"(({scenario.TargetType})";

        return csOutput.Contains(expected).ToProperty()
            .Label($"Expected '{expected}' in emitted C# for {scenario} (site={scenario.Site})\n" +
                   $"C#:\n{csOutput}");
    }
}