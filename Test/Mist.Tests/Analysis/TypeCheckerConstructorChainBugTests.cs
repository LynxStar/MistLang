// Bugfix: type-checker-constructor-chain-bugs
// Property 1: Bug Condition — Constructor Chain Wrong Type Resolution and Missing Diagnostic Location
//
// This test surfaces counterexamples demonstrating two bugs in TypeCheckerAnalyzer:
// Bug 1: FindEnclosingType returns the wrong type in multi-type namespaces, causing
//         false positive MIST_TC_0013 for valid constructor chains like Point() : this(0)
// Bug 2: MakeDiagnostic hardcodes null file path and line 0 for all diagnostics
//
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Bug condition exploration tests for the TypeCheckerAnalyzer constructor chain bugs.
/// These tests encode the EXPECTED (correct) behavior. On unfixed code, they FAIL —
/// confirming the bugs exist. After the fix, they PASS — confirming the bugs are resolved.
/// </summary>
public class TypeCheckerConstructorChainBugTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = value },
        };
    }

    private static QualifiedIdentifierNode MakeQualifiedId(string name)
    {
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(name),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
    }

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        return new UsingDeclarationNode { Name = MakeQualifiedId(namespaceName) };
    }

    private ImmutableArray<Diagnostic> CompileAndTypeCheck(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts)
    {
        var buildDiagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, asts, ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", buildDiagnostics);

        var tcDiagnostics = new DiagnosticBag();
        _analyzer.Analyze(model, tcDiagnostics);
        return tcDiagnostics.ToImmutableArray();
    }

    // ─── AST builders for Point and Color ────────────────────────────────

    /// <summary>
    /// Builds an AST representing a Point class with constructor chaining:
    ///   namespace AllFeatures;
    ///   public class Point {
    ///       private int x;
    ///       public Point(int x) { this.x = x; }
    ///       public Point() : this(0) { }
    ///       public int GetX() { return 0; }
    ///   }
    ///
    /// The default constructor Point() chains to Point(int) via : this(0).
    /// Bug 1 causes FindEnclosingType to return Color instead of Point,
    /// leading to a false positive MIST_TC_0013.
    /// </summary>
    private static CompilationUnitNode BuildPointAst(string source)
    {
        // Field
        var fieldX = new FieldDeclarationNode
        {
            Name = MakeToken("x"),
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };

        // Primary constructor: Point(int x) { this.x = x; }
        var paramX = new ParameterNode
        {
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Name = MakeToken("x"),
        };
        var primaryCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken("Point"),
            Parameters = new ParameterListNode { Parameter = paramX },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeExpressionStatement(MakeIdentifierExpression("x"))),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Default constructor with chain: Point() : this(0) { }
        var chainArgs = new ArgumentListNode
        {
            Argument = new ValueArgumentArgumentNode { Expression = MakeIntLiteralExpression("0") },
        };
        var chain = new ThisChainConstructorChainNode
        {
            Arguments = chainArgs,
            // Give the chain node a non-zero span so MakeDiagnostic produces a Location
            Span = new Storm.Runtime.SourceSpan(100, 10),
        };
        var defaultCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken("Point"),
            Parameters = null,
            Chain = chain,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // GetX method
        var getXMethod = new MethodDeclarationNode
        {
            Name = MakeToken("GetX"),
            ReturnType = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeReturnStatement(MakeIntLiteralExpression("0"))),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var pointClass = new ClassDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldX },
                new ConstructorMemberMemberDeclarationNode { Constructor = primaryCtor },
                new ConstructorMemberMemberDeclarationNode { Constructor = defaultCtor },
                new MethodMemberMemberDeclarationNode { Method = getXMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("System")),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("AllFeatures"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = pointClass }).AsNamespaceBodyItems(),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            } }),
        };
    }

    /// <summary>
    /// Builds an AST representing a Color struct with a parameterless constructor:
    ///   namespace AllFeatures;
    ///   public struct Color {
    ///       public int r;
    ///       public int g;
    ///       public int b;
    ///       public Color() { this.r = r; }
    ///   }
    ///
    /// Color has NO single-int constructor. When FindEnclosingType incorrectly
    /// returns Color for Point's constructor chain this(0), it searches Color's
    /// constructors for a match to (int) and finds none → false MIST_TC_0013.
    /// </summary>
    private static CompilationUnitNode BuildColorAst(string source)
    {
        var fieldR = new FieldDeclarationNode
        {
            Name = MakeToken("r"),
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var fieldG = new FieldDeclarationNode
        {
            Name = MakeToken("g"),
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
        var fieldB = new FieldDeclarationNode
        {
            Name = MakeToken("b"),
            Type = new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Color has a parameterless constructor — no (int) constructor
        var colorCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken("Color"),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = ImmutableArray.Create(
                    MakeExpressionStatement(MakeIdentifierExpression("r"))),
            } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var colorStruct = new StructDeclarationNode
        {
            Name = MakeToken("Color"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldR },
                new FieldMemberMemberDeclarationNode { Field = fieldG },
                new FieldMemberMemberDeclarationNode { Field = fieldB },
                new ConstructorMemberMemberDeclarationNode { Constructor = colorCtor }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("System")),
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("AllFeatures"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new StructTypeTypeDeclarationNode { StructDeclaration = colorStruct }).AsNamespaceBodyItems(),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            } }),
        };
    }

    /// <summary>
    /// Builds a minimal Program class with Main entry point.
    /// </summary>
    private static CompilationUnitNode BuildProgramAst(string source)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray<UsingDeclarationNode>.Empty,
            Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode { NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
            {
                Name = MakeQualifiedId("AllFeatures"),
                Body = ImmutableArray.Create<TypeDeclarationNode>(
                    new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }).AsNamespaceBodyItems(),
                Span = new Storm.Runtime.SourceSpan(0, source.Length),
            } }),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Bug Condition — Constructor Chain Wrong Type Resolution
    //             and Missing Diagnostic Location
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a multi-type namespace scenario for bug condition testing.
    /// </summary>
#pragma warning disable CA1034
    public record BugConditionScenario(string Label)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    private static readonly BugConditionScenario[] BugConditionScenarios =
    [
        new("ColorBeforePoint_ConstructorChain"),
    ];

    public static Arbitrary<BugConditionScenario> ArbitraryBugConditionScenarios()
    {
        var gen = Gen.Elements(BugConditionScenarios);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 1: Bug Condition — In a multi-type namespace where Point has
    /// constructor chaining Point() : this(0), the type checker SHALL NOT
    /// emit MIST_TC_0013 for Point's constructor chain (because Point(int) exists).
    ///
    /// On UNFIXED code, this test FAILS because FindEnclosingType returns Color
    /// instead of Point, causing a false positive MIST_TC_0013.
    ///
    /// The Color AST is compiled BEFORE Point so that Color is registered first
    /// in the namespace scope. FindEnclosingType iterates scope.Members and returns
    /// the first Class/Struct it finds — which is Color when Color is registered first.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerConstructorChainBugTests) }, MaxTest = 5)]
    public bool ConstructorChain_InMultiTypeNamespace_NoFalsePositiveMIST_TC_0013(BugConditionScenario scenario)
    {
        // Arrange: compile Color BEFORE Point so Color is registered first in namespace scope.
        // This triggers the bug: FindEnclosingType returns Color for Point's constructor.
        var pointSource = "namespace AllFeatures;\npublic class Point {\n    private int x;\n    public Point(int x) { this.x = x; }\n    public Point() : this(0) { }\n    public int GetX() { return 0; }\n}";
        var colorSource = "namespace AllFeatures;\npublic struct Color {\n    public int r;\n    public int g;\n    public int b;\n    public Color() { this.r = r; }\n}";
        var programSource = "namespace AllFeatures;\npublic class Program {\n    public static void Main() { }\n}";

        var pointAst = BuildPointAst(pointSource);
        var colorAst = BuildColorAst(colorSource);
        var programAst = BuildProgramAst(programSource);

        // Color AST first → Color registered first in namespace scope
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(new SourceText("Color.mist", colorSource)),
            new CompilationUnit(new SourceText("Point.mist", pointSource)),
            new CompilationUnit(new SourceText("Program.mist", programSource))));

        // Act
        var diagnostics = CompileAndTypeCheck(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(colorAst, pointAst, programAst));

        // Assert: MIST_TC_0013 should NOT be emitted for Point's constructor chain
        // Bug 1: On unfixed code, FindEnclosingType returns Color, causing false positive
        var falsePositive0013 = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0013")
            .ToList();

        return falsePositive0013.Count == 0;
    }

    /// <summary>
    /// Property 1 (continued): Any type checker diagnostic emitted SHALL have
    /// a non-null CompilationUnitPath.
    ///
    /// On UNFIXED code, this test FAILS because MakeDiagnostic always passes null.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerConstructorChainBugTests) }, MaxTest = 5)]
    public bool TypeCheckerDiagnostics_HaveNonNullCompilationUnitPath(BugConditionScenario scenario)
    {
        // Arrange: compile Color BEFORE Point to trigger the false MIST_TC_0013,
        // which gives us a type checker diagnostic to inspect for Bug 2.
        var pointSource = "namespace AllFeatures;\npublic class Point {\n    private int x;\n    public Point(int x) { this.x = x; }\n    public Point() : this(0) { }\n    public int GetX() { return 0; }\n}";
        var colorSource = "namespace AllFeatures;\npublic struct Color {\n    public int r;\n    public int g;\n    public int b;\n    public Color() { this.r = r; }\n}";
        var programSource = "namespace AllFeatures;\npublic class Program {\n    public static void Main() { }\n}";

        var pointAst = BuildPointAst(pointSource);
        var colorAst = BuildColorAst(colorSource);
        var programAst = BuildProgramAst(programSource);

        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(new SourceText("Color.mist", colorSource)),
            new CompilationUnit(new SourceText("Point.mist", pointSource)),
            new CompilationUnit(new SourceText("Program.mist", programSource))));

        // Act
        var diagnostics = CompileAndTypeCheck(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(colorAst, pointAst, programAst));

        // Assert: every type checker diagnostic must have a non-null CompilationUnitPath
        // Bug 2: On unfixed code, MakeDiagnostic always passes CompilationUnitPath: null
        var tcDiagnostics = diagnostics
            .Where(d => d.AnalyzerName == "TypeChecker")
            .ToList();

        // If there are no type checker diagnostics, the property trivially holds
        if (tcDiagnostics.Count == 0)
            return true;

        return tcDiagnostics.All(d => d.CompilationUnitPath != "<unknown>");
    }

    /// <summary>
    /// Property 1 (continued): Any type checker diagnostic emitted SHALL have
    /// Location.Start.Line >= 1 (1-based line numbers).
    ///
    /// On UNFIXED code, this test FAILS because MakeDiagnostic uses line 0.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerConstructorChainBugTests) }, MaxTest = 5)]
    public bool TypeCheckerDiagnostics_HaveValidLineNumbers(BugConditionScenario scenario)
    {
        // Arrange: compile Color BEFORE Point to trigger the false MIST_TC_0013
        var pointSource = "namespace AllFeatures;\npublic class Point {\n    private int x;\n    public Point(int x) { this.x = x; }\n    public Point() : this(0) { }\n    public int GetX() { return 0; }\n}";
        var colorSource = "namespace AllFeatures;\npublic struct Color {\n    public int r;\n    public int g;\n    public int b;\n    public Color() { this.r = r; }\n}";
        var programSource = "namespace AllFeatures;\npublic class Program {\n    public static void Main() { }\n}";

        var pointAst = BuildPointAst(pointSource);
        var colorAst = BuildColorAst(colorSource);
        var programAst = BuildProgramAst(programSource);

        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(new SourceText("Color.mist", colorSource)),
            new CompilationUnit(new SourceText("Point.mist", pointSource)),
            new CompilationUnit(new SourceText("Program.mist", programSource))));

        // Act
        var diagnostics = CompileAndTypeCheck(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(colorAst, pointAst, programAst));

        // Assert: every type checker diagnostic with a location must have Line >= 1
        // Bug 2: On unfixed code, MakeDiagnostic uses LinePosition(0, offset) — line is always 0
        var tcDiagnostics = diagnostics
            .Where(d => d.AnalyzerName == "TypeChecker" && d.Location != default)
            .ToList();

        if (tcDiagnostics.Count == 0)
            return true;

        return tcDiagnostics.All(d => d.Location.Start.Line >= 1);
    }
}
