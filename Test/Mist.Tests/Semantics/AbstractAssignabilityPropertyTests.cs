// Feature: abstracts, Property 8: Abstract subtype assignability
// **Validates: Requirements 8.1, 8.3, 8.4, 8.5**

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
/// Property-based tests verifying abstract subtype assignability.
///
/// Property 8: For any type that implements an abstract (directly or transitively),
/// assigning a value of that type to a variable typed as the implemented abstract
/// SHALL be accepted by the type checker with no diagnostic. This applies to
/// class-to-abstract, struct-to-abstract, and abstract-to-parent-abstract
/// assignments, and extends to method parameter passing and return type checking.
/// </summary>
public class AbstractAssignabilityPropertyTests
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

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
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
                Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
                } },
                Name = MakeToken(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = expr,
            },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode value)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = value,
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
        ImmutableArray<AbstractMethodDeclarationNode> methods,
        string? parentAbstractName = null)
    {
        var members = methods.Select(m =>
            (AbstractMemberDeclarationNode)new AbstractMethodMemberAbstractMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if (parentAbstractName is not null)
        {
            var _abstractList1 = MakeBaseList(parentAbstractName);

            decl.AbstractList = _abstractList1;

            decl.Abstracts = _abstractList1;
        }

        return decl;
    }

    private static MethodDeclarationNode MakeMethodDecl(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null,
        ImmutableArray<StatementNode>? bodyStatements = null,
        bool isStatic = false)
    {
        var modifiers = new List<ModifierNode> { new PublicModifierModifierNode() };
        if (isStatic)
            modifiers.Add(new StaticModifierModifierNode());

        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode
            {
                Statements = bodyStatements ?? ImmutableArray<StatementNode>.Empty,
            } },
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

        if (baseOrAbstractName is not null)
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

        if (abstractName is not null)
        {
            var _abstractList2 = MakeBaseList(abstractName);

            decl.AbstractList = _abstractList2;

            decl.Abstracts = _abstractList2;
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

    /// <summary>
    /// Creates an expression: target.methodName(arg) — a static method call with one argument.
    /// </summary>
    private static ExpressionNode MakeStaticMethodCallExpression(
        string target, string methodName, ExpressionNode arg)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(target),
        };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = arg } },
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
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

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Whether the implementing type is a class or struct.
    /// </summary>
    public enum ImplTypeKind
    {
        Class,
        Struct,
    }

    /// <summary>
    /// How the assignment is performed.
    /// </summary>
    public enum AssignmentKind
    {
        VariableDeclaration,
        MethodParameter,
        ReturnType,
    }

    /// <summary>
    /// A scenario where a class or struct implements an abstract directly
    /// and a value is assigned to an abstract-typed variable.
    /// </summary>
    public record DirectAssignabilityScenario(
        string AbstractName,
        ImmutableArray<string> AbstractMethodNames,
        string ImplementingTypeName,
        ImplTypeKind TypeKind,
        AssignmentKind Kind)
    {
        public override string ToString()
        {
            var methods = AbstractMethodNames.Length > 0
                ? string.Join(", ", AbstractMethodNames)
                : "(none)";
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{Kind}: {kind} {ImplementingTypeName} : {AbstractName} {{ {methods} }}";
        }
    }

    /// <summary>
    /// A scenario where an abstract inherits from another abstract and
    /// a value typed as the child abstract is assigned to the parent abstract.
    /// </summary>
    public record AbstractToParentScenario(
        string ParentAbstractName,
        ImmutableArray<string> ParentMethodNames,
        string ChildAbstractName,
        ImmutableArray<string> ChildMethodNames,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {ImplementingTypeName} : {ChildAbstractName} : {ParentAbstractName}";
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
    /// Generates a scenario where a class or struct directly implements an abstract,
    /// provides all required methods, and a value is assigned to an abstract-typed
    /// variable via variable declaration, method parameter, or return type.
    /// </summary>
    public static Gen<DirectAssignabilityScenario> GenDirectAssignabilityScenario()
    {
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from assignKind in Gen.Elements(
                   AssignmentKind.VariableDeclaration,
                   AssignmentKind.MethodParameter,
                   AssignmentKind.ReturnType)
               from methodCount in Gen.Choose(0, 3)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               select new DirectAssignabilityScenario(
                   AbstractNamePool[abstractIdx],
                   methods,
                   TypeNamePool[typeIdx],
                   typeKind,
                   assignKind);
    }

    /// <summary>
    /// Generates a scenario where an abstract inherits from another abstract,
    /// a type implements the child abstract, and a value is assigned to the
    /// parent abstract (transitive assignability).
    /// </summary>
    public static Gen<AbstractToParentScenario> GenAbstractToParentScenario()
    {
        return from parentIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from childIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               where parentIdx != childIdx
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from parentMethodCount in Gen.Choose(0, 2)
               from childMethodCount in Gen.Choose(0, 2)
               from shuffled in Gen.Shuffle(MethodNamePool)
               let allMethods = shuffled.Take(parentMethodCount + childMethodCount).ToArray()
               let parentMethods = allMethods.Take(parentMethodCount).ToImmutableArray()
               let childMethods = allMethods.Skip(parentMethodCount).Take(childMethodCount).ToImmutableArray()
               select new AbstractToParentScenario(
                   AbstractNamePool[parentIdx],
                   parentMethods,
                   AbstractNamePool[childIdx],
                   childMethods,
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    public static Arbitrary<DirectAssignabilityScenario> ArbitraryDirectAssignabilityScenario() =>
        GenDirectAssignabilityScenario().ToArbitrary();

    public static Arbitrary<AbstractToParentScenario> ArbitraryAbstractToParentScenario() =>
        GenAbstractToParentScenario().ToArbitrary();

    // ─── AST construction for full programs ──────────────────────────────

    /// <summary>
    /// Builds a full program AST for a direct assignability scenario.
    /// The program includes the abstract, the implementing type, and a Program
    /// class with Main that performs the assignment.
    /// </summary>
    private static CompilationUnitNode BuildDirectAssignabilityAst(DirectAssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Abstract declaration with methods
        var abstractMethods = scenario.AbstractMethodNames
            .Select(m => MakeAbstractMethod(m))
            .ToImmutableArray();
        var abstractDecl = MakeAbstractDecl(scenario.AbstractName, abstractMethods);
        typeDecls.Add(WrapAbstract(abstractDecl));

        // 2. Implementing type with matching methods
        var implMembers = scenario.AbstractMethodNames
            .Select(m => WrapMethod(MakeMethodDecl(m)))
            .ToImmutableArray();

        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                typeDecls.Add(WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers)));
                break;
            case ImplTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.AbstractName, implMembers)));
                break;
        }

        // 3. Program class with Main that performs the assignment
        var programMembers = new List<MemberDeclarationNode>();
        var mainStatements = new List<StatementNode>();

        switch (scenario.Kind)
        {
            case AssignmentKind.VariableDeclaration:
                // IFoo x = new Foo();
                mainStatements.Add(MakeNamedTypeVarDecl(
                    scenario.AbstractName, "x", scenario.ImplementingTypeName));
                break;

            case AssignmentKind.MethodParameter:
                // var d = new Foo();
                // Program.AcceptAbstract(d);
                mainStatements.Add(MakeNamedTypeVarDecl(
                    scenario.ImplementingTypeName, "d", scenario.ImplementingTypeName));
                mainStatements.Add(MakeExpressionStatement(
                    MakeStaticMethodCallExpression("Program", "AcceptAbstract",
                        MakeIdentifierExpression("d"))));

                // Add the helper method: static void AcceptAbstract(IFoo param) { }
                var param = new ParameterNode
                {
                    Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken(scenario.AbstractName),
                            Span = new Storm.Runtime.SourceSpan(0, scenario.AbstractName.Length),
                        },
                    } },
                    Name = MakeToken("param"),
                };
                programMembers.Add(WrapMethod(MakeMethodDecl(
                    "AcceptAbstract",
                    parameters: new ParameterListNode { Parameter = param },
                    isStatic: true)));
                break;

            case AssignmentKind.ReturnType:
                // Build a static method that returns IFoo and returns new Foo()
                // static IFoo GetInstance() { return new Foo(); }
                var returnStmt = MakeReturnStatement(
                    MakeNewObjectExpression(scenario.ImplementingTypeName));
                var returnTypeRef = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(scenario.AbstractName),
                        Span = new Storm.Runtime.SourceSpan(0, scenario.AbstractName.Length),
                    },
                } };
                programMembers.Add(WrapMethod(MakeMethodDecl(
                    "GetInstance",
                    returnType: returnTypeRef,
                    bodyStatements: ImmutableArray.Create(returnStmt),
                    isStatic: true)));
                break;
        }

        var mainMethod = MakeMethodDecl(
            "Main",
            bodyStatements: mainStatements.ToImmutableArray(),
            isStatic: true);
        programMembers.Insert(0, WrapMethod(mainMethod));

        var programClass = MakeClassDecl("Program", members: programMembers.ToImmutableArray());
        typeDecls.Add(WrapClass(programClass));

        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a full program AST for an abstract-to-parent assignability scenario.
    /// The implementing type implements the child abstract, and a value is assigned
    /// to a variable typed as the parent abstract (transitive).
    /// </summary>
    private static CompilationUnitNode BuildAbstractToParentAst(AbstractToParentScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Parent abstract
        var parentMethods = scenario.ParentMethodNames
            .Select(m => MakeAbstractMethod(m))
            .ToImmutableArray();
        var parentDecl = MakeAbstractDecl(scenario.ParentAbstractName, parentMethods);
        typeDecls.Add(WrapAbstract(parentDecl));

        // 2. Child abstract inheriting from parent
        var childMethods = scenario.ChildMethodNames
            .Select(m => MakeAbstractMethod(m))
            .ToImmutableArray();
        var childDecl = MakeAbstractDecl(
            scenario.ChildAbstractName, childMethods, scenario.ParentAbstractName);
        typeDecls.Add(WrapAbstract(childDecl));

        // 3. Implementing type with ALL methods (parent + child)
        var allMethodNames = scenario.ParentMethodNames.Concat(scenario.ChildMethodNames);
        var implMembers = allMethodNames
            .Select(m => WrapMethod(MakeMethodDecl(m)))
            .ToImmutableArray();

        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                typeDecls.Add(WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.ChildAbstractName, implMembers)));
                break;
            case ImplTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.ChildAbstractName, implMembers)));
                break;
        }

        // 4. Program class with Main that assigns to the PARENT abstract type
        // IParent x = new Impl();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(
                scenario.ParentAbstractName, "x", scenario.ImplementingTypeName));

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
    /// Property 8a: For any class or struct that directly implements an abstract
    /// and provides all required methods, assigning a value of that type to a
    /// variable typed as the abstract SHALL produce no MIST_TC_0001 diagnostic.
    /// This covers variable declarations, method parameters, and return types.
    ///
    /// **Validates: Requirements 8.1, 8.4, 8.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool DirectAbstractAssignment_ProducesNoTypeMismatchDiagnostic(DirectAssignabilityScenario scenario)
    {
        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted for valid abstract assignments
        return typeMismatchDiags.Count == 0;
    }

    /// <summary>
    /// Property 8b: For any type that implements a child abstract which inherits
    /// from a parent abstract, assigning a value of that type to a variable typed
    /// as the parent abstract SHALL produce no MIST_TC_0001 diagnostic (transitive
    /// abstract assignability).
    ///
    /// **Validates: Requirements 8.1, 8.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool TransitiveAbstractAssignment_ProducesNoTypeMismatchDiagnostic(AbstractToParentScenario scenario)
    {
        var ast = BuildAbstractToParentAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted for valid transitive abstract assignments
        return typeMismatchDiags.Count == 0;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassToAbstract_VariableDeclaration_NoDiagnostic()
    {
        // abstract IWorker { void DoWork(); }
        // class Worker : IWorker { void DoWork() {} }
        // class Program { static void Main() { IWorker w = new Worker(); } }
        var scenario = new DirectAssignabilityScenario(
            "IWorker",
            ImmutableArray.Create("DoWork"),
            "Worker",
            ImplTypeKind.Class,
            AssignmentKind.VariableDeclaration);

        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void StructToAbstract_VariableDeclaration_NoDiagnostic()
    {
        // abstract IComparable { void CompareTo(); }
        // struct Point : IComparable { void CompareTo() {} }
        // class Program { static void Main() { IComparable c = new Point(); } }
        var scenario = new DirectAssignabilityScenario(
            "IComparable",
            ImmutableArray.Create("CompareTo"),
            "Point",
            ImplTypeKind.Struct,
            AssignmentKind.VariableDeclaration);

        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void ClassToAbstract_MethodParameter_NoDiagnostic()
    {
        // abstract IRunner { void Run(); }
        // class Runner : IRunner { void Run() {} }
        // class Program {
        //   static void Main() { var d = new Runner(); Program.AcceptAbstract(d); }
        //   static void AcceptAbstract(IRunner param) {}
        // }
        var scenario = new DirectAssignabilityScenario(
            "IRunner",
            ImmutableArray.Create("Run"),
            "Runner",
            ImplTypeKind.Class,
            AssignmentKind.MethodParameter);

        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void ClassToAbstract_ReturnType_NoDiagnostic()
    {
        // abstract IProcessor { void Process(); }
        // class Processor : IProcessor { void Process() {} }
        // class Program {
        //   static void Main() {}
        //   static IProcessor GetInstance() { return new Processor(); }
        // }
        var scenario = new DirectAssignabilityScenario(
            "IProcessor",
            ImmutableArray.Create("Process"),
            "Processor",
            ImplTypeKind.Class,
            AssignmentKind.ReturnType);

        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void TransitiveAbstractAssignment_ClassToParentAbstract_NoDiagnostic()
    {
        // abstract IBase { void BaseMethod(); }
        // abstract IDerived : IBase { void DerivedMethod(); }
        // class Impl : IDerived { void BaseMethod() {} void DerivedMethod() {} }
        // class Program { static void Main() { IBase x = new Impl(); } }
        var scenario = new AbstractToParentScenario(
            "IBase",
            ImmutableArray.Create("BaseMethod"),
            "IDerived",
            ImmutableArray.Create("DerivedMethod"),
            "Impl",
            ImplTypeKind.Class);

        var ast = BuildAbstractToParentAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void EmptyAbstract_ClassAssignment_NoDiagnostic()
    {
        // abstract IEmpty { }
        // class Impl : IEmpty { }
        // class Program { static void Main() { IEmpty x = new Impl(); } }
        var scenario = new DirectAssignabilityScenario(
            "IEmpty",
            ImmutableArray<string>.Empty,
            "Impl",
            ImplTypeKind.Class,
            AssignmentKind.VariableDeclaration);

        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    ;
    }

}
