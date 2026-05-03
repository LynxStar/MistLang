// Feature: interfaces, Property 8: Interface subtype assignability
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
/// Property-based tests verifying interface subtype assignability.
///
/// Property 8: For any type that implements an interface (directly or transitively),
/// assigning a value of that type to a variable typed as the implemented interface
/// SHALL be accepted by the type checker with no diagnostic. This applies to
/// class-to-interface, struct-to-interface, and interface-to-parent-interface
/// assignments, and extends to method parameter passing and return type checking.
/// </summary>
public class InterfaceAssignabilityPropertyTests
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
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
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
                Type = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(declaredTypeName),
                        Span = new Storm.Runtime.SourceSpan(0, declaredTypeName.Length),
                    },
                },
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

    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(string name)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
        };
    }

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods,
        string? parentInterfaceName = null)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        var decl = new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if (parentInterfaceName is not null)
        {
            var _ifaceList1 = MakeBaseList(parentInterfaceName);

            decl.InterfaceList = _ifaceList1;

            decl.Interfaces = _ifaceList1;
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
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
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
        string? baseOrInterfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (baseOrInterfaceName is not null)
        {
            var _baseList1 = MakeBaseList(baseOrInterfaceName);

            decl.BaseClause = _baseList1;

            decl.BaseList = _baseList1;
        }

        return decl;
    }

    private static StructDeclarationNode MakeStructDecl(
        string name,
        string? interfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if (interfaceName is not null)
        {
            var _ifaceList2 = MakeBaseList(interfaceName);

            decl.InterfaceList = _ifaceList2;

            decl.Interfaces = _ifaceList2;
        }

        return decl;
    }

    private static MemberDeclarationNode WrapMethod(MethodDeclarationNode method) =>
        new MethodMemberMemberDeclarationNode { Method = method };

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

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
            Arguments = new ArgumentListNode { Expression = arg },
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
        var assign = new AssignmentNode { Target = logOr };
        return new ExpressionNode { Assignment = assign };
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
    /// A scenario where a class or struct implements an interface directly
    /// and a value is assigned to an interface-typed variable.
    /// </summary>
    public record DirectAssignabilityScenario(
        string InterfaceName,
        ImmutableArray<string> InterfaceMethodNames,
        string ImplementingTypeName,
        ImplTypeKind TypeKind,
        AssignmentKind Kind)
    {
        public override string ToString()
        {
            var methods = InterfaceMethodNames.Length > 0
                ? string.Join(", ", InterfaceMethodNames)
                : "(none)";
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{Kind}: {kind} {ImplementingTypeName} : {InterfaceName} {{ {methods} }}";
        }
    }

    /// <summary>
    /// A scenario where an interface inherits from another interface and
    /// a value typed as the child interface is assigned to the parent interface.
    /// </summary>
    public record InterfaceToParentScenario(
        string ParentInterfaceName,
        ImmutableArray<string> ParentMethodNames,
        string ChildInterfaceName,
        ImmutableArray<string> ChildMethodNames,
        string ImplementingTypeName,
        ImplTypeKind TypeKind)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {ImplementingTypeName} : {ChildInterfaceName} : {ParentInterfaceName}";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] TypeNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a class or struct directly implements an interface,
    /// provides all required methods, and a value is assigned to an interface-typed
    /// variable via variable declaration, method parameter, or return type.
    /// </summary>
    public static Gen<DirectAssignabilityScenario> GenDirectAssignabilityScenario()
    {
        return from interfaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
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
                   InterfaceNamePool[interfaceIdx],
                   methods,
                   TypeNamePool[typeIdx],
                   typeKind,
                   assignKind);
    }

    /// <summary>
    /// Generates a scenario where an interface inherits from another interface,
    /// a type implements the child interface, and a value is assigned to the
    /// parent interface (transitive assignability).
    /// </summary>
    public static Gen<InterfaceToParentScenario> GenInterfaceToParentScenario()
    {
        return from parentIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from childIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               where parentIdx != childIdx
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from parentMethodCount in Gen.Choose(0, 2)
               from childMethodCount in Gen.Choose(0, 2)
               from shuffled in Gen.Shuffle(MethodNamePool)
               let allMethods = shuffled.Take(parentMethodCount + childMethodCount).ToArray()
               let parentMethods = allMethods.Take(parentMethodCount).ToImmutableArray()
               let childMethods = allMethods.Skip(parentMethodCount).Take(childMethodCount).ToImmutableArray()
               select new InterfaceToParentScenario(
                   InterfaceNamePool[parentIdx],
                   parentMethods,
                   InterfaceNamePool[childIdx],
                   childMethods,
                   TypeNamePool[typeIdx],
                   typeKind);
    }

    public static Arbitrary<DirectAssignabilityScenario> ArbitraryDirectAssignabilityScenario() =>
        GenDirectAssignabilityScenario().ToArbitrary();

    public static Arbitrary<InterfaceToParentScenario> ArbitraryInterfaceToParentScenario() =>
        GenInterfaceToParentScenario().ToArbitrary();

    // ─── AST construction for full programs ──────────────────────────────

    /// <summary>
    /// Builds a full program AST for a direct assignability scenario.
    /// The program includes the interface, the implementing type, and a Program
    /// class with Main that performs the assignment.
    /// </summary>
    private static CompilationUnitNode BuildDirectAssignabilityAst(DirectAssignabilityScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Interface declaration with methods
        var interfaceMethods = scenario.InterfaceMethodNames
            .Select(m => MakeInterfaceMethod(m))
            .ToImmutableArray();
        var interfaceDecl = MakeInterfaceDecl(scenario.InterfaceName, interfaceMethods);
        typeDecls.Add(WrapInterface(interfaceDecl));

        // 2. Implementing type with matching methods
        var implMembers = scenario.InterfaceMethodNames
            .Select(m => WrapMethod(MakeMethodDecl(m)))
            .ToImmutableArray();

        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                typeDecls.Add(WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers)));
                break;
            case ImplTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.InterfaceName, implMembers)));
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
                    scenario.InterfaceName, "x", scenario.ImplementingTypeName));
                break;

            case AssignmentKind.MethodParameter:
                // var d = new Foo();
                // Program.AcceptInterface(d);
                mainStatements.Add(MakeNamedTypeVarDecl(
                    scenario.ImplementingTypeName, "d", scenario.ImplementingTypeName));
                mainStatements.Add(MakeExpressionStatement(
                    MakeStaticMethodCallExpression("Program", "AcceptInterface",
                        MakeIdentifierExpression("d"))));

                // Add the helper method: static void AcceptInterface(IFoo param) { }
                var param = new ParameterNode
                {
                    Type = new NamedTypeRefTypeReferenceNode
                    {
                        Name = new QualifiedIdentifierNode
                        {
                            IDENTIFIER = MakeToken(scenario.InterfaceName),
                            Span = new Storm.Runtime.SourceSpan(0, scenario.InterfaceName.Length),
                        },
                    },
                    Name = MakeToken("param"),
                };
                programMembers.Add(WrapMethod(MakeMethodDecl(
                    "AcceptInterface",
                    parameters: new ParameterListNode { Parameter = param },
                    isStatic: true)));
                break;

            case AssignmentKind.ReturnType:
                // Build a static method that returns IFoo and returns new Foo()
                // static IFoo GetInstance() { return new Foo(); }
                var returnStmt = MakeReturnStatement(
                    MakeNewObjectExpression(scenario.ImplementingTypeName));
                var returnTypeRef = new NamedTypeRefTypeReferenceNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(scenario.InterfaceName),
                        Span = new Storm.Runtime.SourceSpan(0, scenario.InterfaceName.Length),
                    },
                };
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
    /// Builds a full program AST for an interface-to-parent assignability scenario.
    /// The implementing type implements the child interface, and a value is assigned
    /// to a variable typed as the parent interface (transitive).
    /// </summary>
    private static CompilationUnitNode BuildInterfaceToParentAst(InterfaceToParentScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // 1. Parent interface
        var parentMethods = scenario.ParentMethodNames
            .Select(m => MakeInterfaceMethod(m))
            .ToImmutableArray();
        var parentDecl = MakeInterfaceDecl(scenario.ParentInterfaceName, parentMethods);
        typeDecls.Add(WrapInterface(parentDecl));

        // 2. Child interface inheriting from parent
        var childMethods = scenario.ChildMethodNames
            .Select(m => MakeInterfaceMethod(m))
            .ToImmutableArray();
        var childDecl = MakeInterfaceDecl(
            scenario.ChildInterfaceName, childMethods, scenario.ParentInterfaceName);
        typeDecls.Add(WrapInterface(childDecl));

        // 3. Implementing type with ALL methods (parent + child)
        var allMethodNames = scenario.ParentMethodNames.Concat(scenario.ChildMethodNames);
        var implMembers = allMethodNames
            .Select(m => WrapMethod(MakeMethodDecl(m)))
            .ToImmutableArray();

        switch (scenario.TypeKind)
        {
            case ImplTypeKind.Class:
                typeDecls.Add(WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.ChildInterfaceName, implMembers)));
                break;
            case ImplTypeKind.Struct:
                typeDecls.Add(WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.ChildInterfaceName, implMembers)));
                break;
        }

        // 4. Program class with Main that assigns to the PARENT interface type
        // IParent x = new Impl();
        var mainStatements = ImmutableArray.Create(
            MakeNamedTypeVarDecl(
                scenario.ParentInterfaceName, "x", scenario.ImplementingTypeName));

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
    /// Property 8a: For any class or struct that directly implements an interface
    /// and provides all required methods, assigning a value of that type to a
    /// variable typed as the interface SHALL produce no MIST_TC_0001 diagnostic.
    /// This covers variable declarations, method parameters, and return types.
    ///
    /// **Validates: Requirements 8.1, 8.4, 8.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool DirectInterfaceAssignment_ProducesNoTypeMismatchDiagnostic(DirectAssignabilityScenario scenario)
    {
        var ast = BuildDirectAssignabilityAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted for valid interface assignments
        return typeMismatchDiags.Count == 0;
    }

    /// <summary>
    /// Property 8b: For any type that implements a child interface which inherits
    /// from a parent interface, assigning a value of that type to a variable typed
    /// as the parent interface SHALL produce no MIST_TC_0001 diagnostic (transitive
    /// interface assignability).
    ///
    /// **Validates: Requirements 8.1, 8.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceAssignabilityPropertyTests) }, MaxTest = 100)]
    public bool TransitiveInterfaceAssignment_ProducesNoTypeMismatchDiagnostic(InterfaceToParentScenario scenario)
    {
        var ast = BuildInterfaceToParentAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Filter to only MIST_TC_0001 diagnostics (type mismatch / cannot assign)
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001")
            .ToList();

        // No type mismatch diagnostic should be emitted for valid transitive interface assignments
        return typeMismatchDiags.Count == 0;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassToInterface_VariableDeclaration_NoDiagnostic()
    {
        // interface IWorker { void DoWork(); }
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
    public void StructToInterface_VariableDeclaration_NoDiagnostic()
    {
        // interface IComparable { void CompareTo(); }
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
    public void ClassToInterface_MethodParameter_NoDiagnostic()
    {
        // interface IRunner { void Run(); }
        // class Runner : IRunner { void Run() {} }
        // class Program {
        //   static void Main() { var d = new Runner(); Program.AcceptInterface(d); }
        //   static void AcceptInterface(IRunner param) {}
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
    public void ClassToInterface_ReturnType_NoDiagnostic()
    {
        // interface IProcessor { void Process(); }
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
    public void TransitiveInterfaceAssignment_ClassToParentInterface_NoDiagnostic()
    {
        // interface IBase { void BaseMethod(); }
        // interface IDerived : IBase { void DerivedMethod(); }
        // class Impl : IDerived { void BaseMethod() {} void DerivedMethod() {} }
        // class Program { static void Main() { IBase x = new Impl(); } }
        var scenario = new InterfaceToParentScenario(
            "IBase",
            ImmutableArray.Create("BaseMethod"),
            "IDerived",
            ImmutableArray.Create("DerivedMethod"),
            "Impl",
            ImplTypeKind.Class);

        var ast = BuildInterfaceToParentAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Fact]
    public void EmptyInterface_ClassAssignment_NoDiagnostic()
    {
        // interface IEmpty { }
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
