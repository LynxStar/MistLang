// Feature: abstracts, Property 9: Non-implementing type is not assignable to abstract
// **Validates: Requirements 8.2**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that non-implementing types are not assignable to abstracts.
///
/// Property 9: For any type that does not implement a given abstract (neither directly
/// nor transitively), assigning a value of that type to a variable typed as that abstract
/// SHALL cause the type checker to emit a diagnostic error.
/// </summary>
public class AbstractNonAssignabilityPropertyTests
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

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty
        };
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Expression builders ─────────────────────────────────────────────

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
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    // ─── Statement builders ──────────────────────────────────────────────

    /// <summary>
    /// Creates a variable declaration: DeclaredType varName = new InitType();
    /// </summary>
    private static StatementNode MakeNamedTypeVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken(declaredTypeName),
                            Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                        },
                    }
                },
                Name = MakeToken(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static AbstractMethodDeclarationNode MakeAbstractMethod(string name)
    {
        return new AbstractMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
        };
    }

    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<AbstractMethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (AbstractMemberDeclarationNode)new AbstractMethodMemberAbstractMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        return new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null,
        ImmutableArray<StatementNode>? bodyStatements = null,
        bool isStatic = false)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if(isStatic)
            modifiers.Add(new StaticModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
                }
            },
            Modifiers = modifiers.ToImmutableArray(),
        };
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? baseOrAbstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseOrAbstractName is not null)
        {
            var _baseList1 = MakeBaseList(baseOrAbstractName);

            decl.BaseClause = _baseList1;

            decl.BaseList = _baseList1;
        }

        return decl;
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? abstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(abstractName is not null)
        {
            var _abstractList1 = MakeBaseList(abstractName);

            decl.AbstractList = _abstractList1;

            decl.Abstracts = _abstractList1;
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    private static TypeDeclarationNode WrapAbstract(AbstractDeclarationNode decl) =>
        new AbstractTypeTypeDeclarationNode { AbstractDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Whether the non-implementing type is a class or struct.
    /// </summary>
    public enum NonImplTypeKind
    {
        Class,
        Struct,
    }

    /// <summary>
    /// A scenario where a class or struct does NOT implement an abstract,
    /// and a value of that type is assigned to an abstract-typed variable.
    /// This should always produce a MIST_TC_0001 diagnostic.
    /// </summary>
    public record NonAssignabilityScenario(
        string AbstractName,
        ImmutableArray<string> AbstractMethodNames,
        string NonImplementingTypeName,
        NonImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var methods = AbstractMethodNames.Length > 0
                ? string.Join(", ", AbstractMethodNames)
                : "(none)";
            var kind = TypeKind == NonImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {NonImplementingTypeName} (no impl) -> {AbstractName} {{ {methods} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] TypeNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a class or struct does NOT implement an abstract
    /// (no abstract list at all), and a value is assigned to an abstract-typed variable.
    /// The non-implementing type has no `: IFoo` clause.
    /// </summary>
    public static Gen<NonAssignabilityScenario> GenNonAssignabilityScenario()
    {
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(NonImplTypeKind.Class, NonImplTypeKind.Struct)
               from methodCount in Gen.Choose(0, 3)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               select new NonAssignabilityScenario(
                   AbstractNamePool[abstractIdx],
                   methods,
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    public static Arbitrary<NonAssignabilityScenario> ArbitraryNonAssignabilityScenario() =>
        GenNonAssignabilityScenario().ToArbitrary();

    // ─── AST construction for full programs ──────────────────────────────

    /// <summary>
    /// Builds a full program AST for a non-assignability scenario.
    /// The program includes:
    /// 1. An abstract declaration with methods
    /// 2. A class or struct that does NOT implement the abstract (no `: IFoo`)
    /// 3. A Program class with Main that assigns the non-implementing type to
    ///    an abstract-typed variable: IFoo x = new Bar();
    /// This should always produce a MIST_TC_0001 diagnostic.
    /// </summary>
    private static CompilationUnitNode BuildNonAssignabilityAst(NonAssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Abstract declaration with methods
        var abstractMethods = scenario.AbstractMethodNames
            .Select(m => MakeAbstractMethod(m))
            .ToImmutableArray();
        var abstractDecl = MakeAbstractDecl(scenario.AbstractName, abstractMethods);
        typeDecls.Add(WrapAbstract(abstractDecl));

        // 2. Non-implementing type — NO abstract list (no `: IFoo`)
        // The type is a plain class or struct with no base clause / abstract list.
        switch(scenario.TypeKind)
        {
            case NonImplTypeKind.Class:
                typeDecls.Add(WrapClass(
                    MakeClassDecl(scenario.NonImplementingTypeName)));
                break;
            case NonImplTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.NonImplementingTypeName)));
                break;
        }

        // 3. Program class with Main that assigns non-implementing type to abstract variable
        // IFoo x = new Bar();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(
                scenario.AbstractName, "x", scenario.NonImplementingTypeName));

        var mainMethod = MakeMethodDecl(
            "Main",
            bodyStatements: mainStatements,
            isStatic: true);
        var programClass = MakeClassDecl("Program",
            members: ImmutableArray.Create<MemberDeclarationNode>(WrapMethod(mainMethod)));
        typeDecls.Add(WrapClass(programClass));

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 9: For any type that does not implement a given abstract (neither
    /// directly nor transitively), assigning a value of that type to a variable typed
    /// as that abstract SHALL cause the type checker to emit a MIST_TC_0001 diagnostic error.
    ///
    /// **Validates: Requirements 8.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractNonAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool NonImplementingType_AssignedToAbstract_ProducesTypeMismatchDiagnostic(NonAssignabilityScenario scenario)
    {
        var ast = BuildNonAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // At least one type mismatch diagnostic should be emitted
        return typeMismatchDiags.Count > 0;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassNotImplementingAbstract_VariableDeclaration_ProducesDiagnostic()
    {
        // abstract IWorker { void DoWork(); }
        // class Foo { }
        // class Program { static void Main() { IWorker w = new Foo(); } }
        var scenario = new NonAssignabilityScenario(
            "IWorker",
            ImmutableArray.Create("DoWork"),
            "Foo",
            NonImplTypeKind.Class);

        var ast = BuildNonAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void StructNotImplementingAbstract_VariableDeclaration_ProducesDiagnostic()
    {
        // abstract IComparable { void CompareTo(); }
        // struct Point { }
        // class Program { static void Main() { IComparable c = new Point(); } }
        var scenario = new NonAssignabilityScenario(
            "IComparable",
            ImmutableArray.Create("CompareTo"),
            "Point",
            NonImplTypeKind.Struct);

        var ast = BuildNonAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void ClassNotImplementingEmptyAbstract_VariableDeclaration_ProducesDiagnostic()
    {
        // abstract IEmpty { }
        // class Foo { }
        // class Program { static void Main() { IEmpty x = new Foo(); } }
        var scenario = new NonAssignabilityScenario(
            "IEmpty",
            ImmutableArray<string>.Empty,
            "Foo",
            NonImplTypeKind.Class);

        var ast = BuildNonAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
        ;
    }

}