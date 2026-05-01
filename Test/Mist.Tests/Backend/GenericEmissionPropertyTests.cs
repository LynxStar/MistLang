// Feature: generics-type-arguments, Property 12: C# emission correctness for generics
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**

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
/// Property-based tests verifying C# emission correctness for generics.
///
/// Property 12: For any valid Mist program containing generic type declarations,
/// generic method declarations, type references with type arguments, object creation
/// expressions with type arguments, method invocations with type arguments, generic
/// inheritance clauses, and nested generic type arguments, the emitted C# code SHALL
/// contain syntactically correct angle bracket syntax that mirrors the Mist source
/// structure.
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**
/// </summary>
public sealed partial class GenericEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public GenericEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistGenericEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    private static TypeParameterListNode MakeTypeParamList(params string[] names)
    {
        var first = new TypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => new TypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeReference = typeRefs[0],
            TypeReferences = typeRefs.Skip(1).ToImmutableArray(),
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new NamedTypeRefTypeReferenceNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
            TypeArgs = typeArgs,
        };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveNode };
    }

    private static BaseListNode MakeBaseList(BaseTypeRefNode[] entries)
    {
        return new BaseListNode
        {
            First = entries[0],
            Rest = entries[0],
            BaseTypeRefs = entries.Skip(1).ToImmutableArray(),
        };
    }

    private static BaseTypeRefNode MakeBaseTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new BaseTypeRefNode { Name = Tok(name), TypeArgs = typeArgs };
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

    private static ExpressionNode MakeNewGenericObjectExpression(string typeName, TypeArgumentListNode typeArgs)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
            },
            TypeArgs = typeArgs,
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        return WrapPrimaryAsExpression(primary);
    }

    /// <summary>
    /// Creates an expression: identifier.methodName&lt;typeArgs&gt;()
    /// </summary>
    private static ExpressionNode MakeGenericMethodCallExpression(
        string identifier, string methodName, TypeArgumentListNode typeArgs)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(identifier) };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(methodName),
            TypeArgs = typeArgs,
            Arguments = null,
        };
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

    private static StatementNode MakeTypedVarDecl(TypeReferenceNode typeRef, string varName, ExpressionNode initializer)
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

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
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

        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        if (Directory.Exists(_outputDir))
        {
            foreach (var file in Directory.GetFiles(_outputDir))
                File.Delete(file);
        }
    }

    private static ClassDeclarationNode MakeProgramClass(ImmutableArray<StatementNode> mainStatements)
    {
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

        return new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a generated test scenario covering various generic emission contexts.
    /// </summary>
#pragma warning disable CA1034
    public enum GenericEmissionKind
    {
        /// <summary>Generic class declaration: class Box&lt;T&gt; { T value; }</summary>
        GenericClassDeclaration,
        /// <summary>Generic struct declaration: struct Pair&lt;T, U&gt; { }</summary>
        GenericStructDeclaration,
        /// <summary>Generic interface declaration: interface IContainer&lt;T&gt; { }</summary>
        GenericInterfaceDeclaration,
        /// <summary>Generic method declaration: T Identity&lt;T&gt;(T value) { return value; }</summary>
        GenericMethodDeclaration,
        /// <summary>Object creation with type args: new Box&lt;int&gt;()</summary>
        ObjectCreationWithTypeArgs,
        /// <summary>Method invocation with type args: obj.Identity&lt;int&gt;()</summary>
        MethodInvocationWithTypeArgs,
        /// <summary>Generic inheritance: class Derived&lt;T&gt; : Base&lt;T&gt; { }</summary>
        GenericInheritance,
        /// <summary>Nested generics: Box&lt;Box&lt;int&gt;&gt;</summary>
        NestedGenerics,
    }

#pragma warning disable CA1819
    public record GenericEmissionScenario(
        GenericEmissionKind Kind,
        string TypeName,
        string[] TypeParams,
        string ConcreteType,
        string? BaseTypeName,
        string? InterfaceName)
#pragma warning restore CA1819
    {
        public override string ToString()
        {
            var tpList = string.Join(", ", TypeParams);
            return Kind switch
            {
                GenericEmissionKind.GenericClassDeclaration =>
                    $"class {TypeName}<{tpList}> {{ {TypeParams[0]} value; }}",
                GenericEmissionKind.GenericStructDeclaration =>
                    $"struct {TypeName}<{tpList}> {{ }}",
                GenericEmissionKind.GenericInterfaceDeclaration =>
                    $"interface {TypeName}<{tpList}> {{ }}",
                GenericEmissionKind.GenericMethodDeclaration =>
                    $"class {TypeName} {{ T DoIt<T>() {{ }} }}",
                GenericEmissionKind.ObjectCreationWithTypeArgs =>
                    $"new {TypeName}<{ConcreteType}>()",
                GenericEmissionKind.MethodInvocationWithTypeArgs =>
                    $"obj.DoIt<{ConcreteType}>()",
                GenericEmissionKind.GenericInheritance =>
                    $"class {TypeName}<T> : {BaseTypeName}<T> {{ }}",
                GenericEmissionKind.NestedGenerics =>
                    $"{TypeName}<{TypeName}<{ConcreteType}>>",
                _ => "unknown",
            };
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool = ["Box", "Container", "Wrapper", "Holder", "Store"];
    private static readonly string[] StructNamePool = ["Pair", "ValueHolder", "Point"];
    private static readonly string[] InterfaceNamePool = ["IKeeper", "IStore", "IHolder"];
    private static readonly string[] PrimitivePool = ["int", "string", "bool", "float"];

    private static Gen<GenericEmissionScenario> GenClassDeclaration()
    {
        return from name in Gen.Elements(ClassNamePool)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = Enumerable.Range(0, paramCount)
                   .Select(i => ((char)('T' + i)).ToString())
                   .ToArray()
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.GenericClassDeclaration,
                   name, typeParams, concreteType, null, null);
    }

    private static Gen<GenericEmissionScenario> GenStructDeclaration()
    {
        return from name in Gen.Elements(StructNamePool)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = Enumerable.Range(0, paramCount)
                   .Select(i => ((char)('T' + i)).ToString())
                   .ToArray()
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.GenericStructDeclaration,
                   name, typeParams, concreteType, null, null);
    }

    private static Gen<GenericEmissionScenario> GenInterfaceDeclaration()
    {
        return from name in Gen.Elements(InterfaceNamePool)
               from paramCount in Gen.Choose(1, 2)
               let typeParams = Enumerable.Range(0, paramCount)
                   .Select(i => ((char)('T' + i)).ToString())
                   .ToArray()
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.GenericInterfaceDeclaration,
                   name, typeParams, concreteType, null, null);
    }

    private static Gen<GenericEmissionScenario> GenMethodDeclaration()
    {
        return from name in Gen.Elements(ClassNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.GenericMethodDeclaration,
                   name, ["T"], concreteType, null, null);
    }

    private static Gen<GenericEmissionScenario> GenObjectCreation()
    {
        return from name in Gen.Elements(ClassNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.ObjectCreationWithTypeArgs,
                   name, ["T"], concreteType, null, null);
    }

    private static Gen<GenericEmissionScenario> GenMethodInvocation()
    {
        return from name in Gen.Elements(ClassNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.MethodInvocationWithTypeArgs,
                   name, ["T"], concreteType, null, null);
    }

    private static Gen<GenericEmissionScenario> GenInheritance()
    {
        return from baseName in Gen.Elements(ClassNamePool)
               from derivedIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               let derivedName = "Sub" + ClassNamePool[derivedIdx]
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.GenericInheritance,
                   derivedName, ["T"], concreteType, baseName, null);
    }

    private static Gen<GenericEmissionScenario> GenNestedGenerics()
    {
        return from name in Gen.Elements(ClassNamePool)
               from concreteType in Gen.Elements(PrimitivePool)
               select new GenericEmissionScenario(
                   GenericEmissionKind.NestedGenerics,
                   name, ["T"], concreteType, null, null);
    }

    public static Gen<GenericEmissionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenClassDeclaration(),
            GenStructDeclaration(),
            GenInterfaceDeclaration(),
            GenMethodDeclaration(),
            GenObjectCreation(),
            GenMethodInvocation(),
            GenInheritance(),
            GenNestedGenerics());
    }

    public static Arbitrary<GenericEmissionScenario> ArbitraryGenericEmissionScenario() =>
        GenScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    private static (CompilationUnitNode Ast, string Source) BuildScenario(GenericEmissionScenario scenario)
    {
        return scenario.Kind switch
        {
            GenericEmissionKind.GenericClassDeclaration => BuildGenericClassDecl(scenario),
            GenericEmissionKind.GenericStructDeclaration => BuildGenericStructDecl(scenario),
            GenericEmissionKind.GenericInterfaceDeclaration => BuildGenericInterfaceDecl(scenario),
            GenericEmissionKind.GenericMethodDeclaration => BuildGenericMethodDecl(scenario),
            GenericEmissionKind.ObjectCreationWithTypeArgs => BuildObjectCreation(scenario),
            GenericEmissionKind.MethodInvocationWithTypeArgs => BuildMethodInvocation(scenario),
            GenericEmissionKind.GenericInheritance => BuildGenericInheritance(scenario),
            GenericEmissionKind.NestedGenerics => BuildNestedGenerics(scenario),
            _ => throw new InvalidOperationException(),
        };
    }

    /// <summary>
    /// Builds: class TypeName&lt;T, U, ...&gt; { public T value; }
    ///         class Program { public static void Main() { } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildGenericClassDecl(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        var fieldMember = new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = Tok("value"),
                Type = MakeNamedTypeRef(scenario.TypeParams[0]),
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            },
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParams),
            Members = ImmutableArray.Create<MemberDeclarationNode>(fieldMember),
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(ImmutableArray<StatementNode>.Empty) });

        var tpList = string.Join(", ", scenario.TypeParams);
        var source = $"class {scenario.TypeName}<{tpList}> {{ public {scenario.TypeParams[0]} value; }}\nclass Program {{ public static void Main() {{ }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: struct TypeName&lt;T, U, ...&gt; { }
    ///         class Program { public static void Main() { } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildGenericStructDecl(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        var structDecl = new StructDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParams),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(ImmutableArray<StatementNode>.Empty) });

        var tpList = string.Join(", ", scenario.TypeParams);
        var source = $"struct {scenario.TypeName}<{tpList}> {{ }}\nclass Program {{ public static void Main() {{ }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: interface TypeName&lt;T, U, ...&gt; { }
    ///         class Program { public static void Main() { } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildGenericInterfaceDecl(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        var interfaceDecl = new InterfaceDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParams),
            Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl });
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(ImmutableArray<StatementNode>.Empty) });

        var tpList = string.Join(", ", scenario.TypeParams);
        var source = $"interface {scenario.TypeName}<{tpList}> {{ }}\nclass Program {{ public static void Main() {{ }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: class TypeName { public T DoIt&lt;T&gt;(T input) { return input; } }
    ///         class Program { public static void Main() { } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildGenericMethodDecl(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        var paramNode = new ParameterNode
        {
            Type = MakeNamedTypeRef("T"),
            Name = Tok("input"),
        };
        var paramList = new ParameterListNode { Parameter = paramNode };

        var returnExpr = MakeIdentifierExpression("input");
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = returnExpr },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = Tok("DoIt"),
            TypeParams = MakeTypeParamList("T"),
            ReturnType = MakeNamedTypeRef("T"),
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(returnStmt) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(ImmutableArray<StatementNode>.Empty) });

        var source = $"class {scenario.TypeName} {{ public T DoIt<T>(T input) {{ return input; }} }}\nclass Program {{ public static void Main() {{ }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: class TypeName&lt;T&gt; { }
    ///         class Program { public static void Main() { TypeName&lt;concrete&gt; x = new TypeName&lt;concrete&gt;(); } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildObjectCreation(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var constructedTypeRef = MakeNamedTypeRef(scenario.TypeName, typeArgList);
        var newExpr = MakeNewGenericObjectExpression(scenario.TypeName, typeArgList);
        var varDecl = MakeTypedVarDecl(constructedTypeRef, "x", newExpr);

        typeDecls.Add(new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = MakeProgramClass(ImmutableArray.Create(varDecl)),
        });

        var source = $"class {scenario.TypeName}<T> {{ }}\nclass Program {{ public static void Main() {{ {scenario.TypeName}<{scenario.ConcreteType}> x = new {scenario.TypeName}<{scenario.ConcreteType}>(); }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: class TypeName { public T DoIt&lt;T&gt;(T input) { return input; } }
    ///         class Program { public static void Main() { TypeName obj = new TypeName(); obj.DoIt&lt;concrete&gt;(default); } }
    /// Note: We simplify by just checking the emitted method call syntax contains the type args.
    /// </summary>
    private static (CompilationUnitNode, string) BuildMethodInvocation(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Class with generic method
        var paramNode = new ParameterNode
        {
            Type = MakeNamedTypeRef("T"),
            Name = Tok("input"),
        };
        var paramList = new ParameterListNode { Parameter = paramNode };

        var returnExpr = MakeIdentifierExpression("input");
        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = returnExpr },
        };

        var methodDecl = new MethodDeclarationNode
        {
            Name = Tok("DoIt"),
            TypeParams = MakeTypeParamList("T"),
            ReturnType = MakeNamedTypeRef("T"),
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(returnStmt) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = methodDecl }),
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        // Main: TypeName obj = new TypeName(); obj.DoIt<concrete>();
        var newObjExpr = WrapPrimaryAsExpression(new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(scenario.TypeName),
            },
            Arguments = null,
        });
        var varDeclObj = MakeTypedVarDecl(MakeNamedTypeRef(scenario.TypeName), "obj", newObjExpr);

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var methodCallExpr = MakeGenericMethodCallExpression("obj", "DoIt", typeArgList);
        var callStmt = MakeExpressionStatement(methodCallExpr);

        typeDecls.Add(new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = MakeProgramClass(ImmutableArray.Create(varDeclObj, callStmt)),
        });

        var source = $"class {scenario.TypeName} {{ public T DoIt<T>(T input) {{ return input; }} }}\nclass Program {{ public static void Main() {{ {scenario.TypeName} obj = new {scenario.TypeName}(); obj.DoIt<{scenario.ConcreteType}>(); }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: class Base&lt;T&gt; { }
    ///         class Derived&lt;T&gt; : Base&lt;T&gt; { }
    ///         class Program { public static void Main() { } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildGenericInheritance(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        // Base class
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.BaseTypeName!),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl });

        // Derived class: Derived<T> : Base<T>
        var baseTypeArgList = MakeTypeArgList([MakeNamedTypeRef("T")]);
        var baseList = MakeBaseList([MakeBaseTypeRef(scenario.BaseTypeName!, baseTypeArgList)]);
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = MakeProgramClass(ImmutableArray<StatementNode>.Empty) });

        var source = $"class {scenario.BaseTypeName}<T> {{ }}\nclass {scenario.TypeName}<T> : {scenario.BaseTypeName}<T> {{ }}\nclass Program {{ public static void Main() {{ }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    /// <summary>
    /// Builds: class TypeName&lt;T&gt; { }
    ///         class Program { public static void Main() { TypeName&lt;TypeName&lt;concrete&gt;&gt; x = new TypeName&lt;TypeName&lt;concrete&gt;&gt;(); } }
    /// </summary>
    private static (CompilationUnitNode, string) BuildNestedGenerics(GenericEmissionScenario scenario)
    {
        var typeDecls = new List<TypeDeclarationNode>();

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.TypeName),
            TypeParams = MakeTypeParamList("T"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        typeDecls.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        // Build nested type arg: TypeName<concrete>
        var innerTypeArgList = MakeTypeArgList([MakePrimitiveTypeRef(scenario.ConcreteType)]);
        var innerTypeRef = MakeNamedTypeRef(scenario.TypeName, innerTypeArgList);

        // Build outer type arg: TypeName<TypeName<concrete>>
        var outerTypeArgList = MakeTypeArgList([innerTypeRef]);
        var outerTypeRef = MakeNamedTypeRef(scenario.TypeName, outerTypeArgList);
        var newExpr = MakeNewGenericObjectExpression(scenario.TypeName, outerTypeArgList);
        var varDecl = MakeTypedVarDecl(outerTypeRef, "x", newExpr);

        typeDecls.Add(new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = MakeProgramClass(ImmutableArray.Create(varDecl)),
        });

        var source = $"class {scenario.TypeName}<T> {{ }}\nclass Program {{ public static void Main() {{ {scenario.TypeName}<{scenario.TypeName}<{scenario.ConcreteType}>> x = new {scenario.TypeName}<{scenario.TypeName}<{scenario.ConcreteType}>>(); }} }}";
        return (new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() }, source);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 12: C# emission correctness for generics
    //
    // **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 12.8**
    //
    // For any valid Mist program containing generic type declarations,
    // generic method declarations, type references with type arguments,
    // object creation expressions with type arguments, method invocations
    // with type arguments, generic inheritance clauses, and nested generic
    // type arguments, the emitted C# code SHALL contain syntactically
    // correct angle bracket syntax.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(GenericEmissionPropertyTests) })]
    public bool CSharpEmission_ContainsCorrectAngleBracketSyntax(GenericEmissionScenario scenario)
    {
        CleanOutputDir();

        var (ast, source) = BuildScenario(scenario);
        var model = BuildModel(ast, source);
        var csContent = EmitAndGetCsContent(model);

        if (string.IsNullOrEmpty(csContent))
            return false;

        return scenario.Kind switch
        {
            GenericEmissionKind.GenericClassDeclaration => VerifyGenericClassDecl(csContent, scenario),
            GenericEmissionKind.GenericStructDeclaration => VerifyGenericStructDecl(csContent, scenario),
            GenericEmissionKind.GenericInterfaceDeclaration => VerifyGenericInterfaceDecl(csContent, scenario),
            GenericEmissionKind.GenericMethodDeclaration => VerifyGenericMethodDecl(csContent, scenario),
            GenericEmissionKind.ObjectCreationWithTypeArgs => VerifyObjectCreation(csContent, scenario),
            GenericEmissionKind.MethodInvocationWithTypeArgs => VerifyMethodInvocation(csContent, scenario),
            GenericEmissionKind.GenericInheritance => VerifyGenericInheritance(csContent, scenario),
            GenericEmissionKind.NestedGenerics => VerifyNestedGenerics(csContent, scenario),
            _ => false,
        };
    }

    // ─── Verification helpers ────────────────────────────────────────────

    /// <summary>
    /// Req 12.1: Emitter SHALL emit generic type declarations with type parameter list.
    /// Verifies: class TypeName&lt;T, U&gt; appears in emitted C#.
    /// </summary>
    private static bool VerifyGenericClassDecl(string csContent, GenericEmissionScenario scenario)
    {
        var tpList = string.Join(", ", scenario.TypeParams);
        var expected = $"class {scenario.TypeName}<{tpList}>";
        return csContent.Contains(expected);
    }

    /// <summary>
    /// Req 12.1: Emitter SHALL emit generic struct declarations with type parameter list.
    /// Verifies: struct TypeName&lt;T, U&gt; appears in emitted C#.
    /// </summary>
    private static bool VerifyGenericStructDecl(string csContent, GenericEmissionScenario scenario)
    {
        var tpList = string.Join(", ", scenario.TypeParams);
        var expected = $"struct {scenario.TypeName}<{tpList}>";
        return csContent.Contains(expected);
    }

    /// <summary>
    /// Req 12.1: Emitter SHALL emit generic interface declarations with type parameter list.
    /// Verifies: interface TypeName&lt;T, U&gt; appears in emitted C#.
    /// </summary>
    private static bool VerifyGenericInterfaceDecl(string csContent, GenericEmissionScenario scenario)
    {
        var tpList = string.Join(", ", scenario.TypeParams);
        var expected = $"interface {scenario.TypeName}<{tpList}>";
        return csContent.Contains(expected);
    }

    /// <summary>
    /// Req 12.2: Emitter SHALL emit generic method declarations with type parameter list.
    /// Verifies: DoIt&lt;T&gt; appears in emitted C#.
    /// </summary>
    private static bool VerifyGenericMethodDecl(string csContent, GenericEmissionScenario scenario)
    {
        return csContent.Contains("DoIt<T>(");
    }

    /// <summary>
    /// Req 12.3, 12.4: Emitter SHALL emit type references and object creation with type arguments.
    /// Verifies: new TypeName&lt;concrete&gt;() appears in emitted C#.
    /// </summary>
    private static bool VerifyObjectCreation(string csContent, GenericEmissionScenario scenario)
    {
        var expectedNew = $"new {scenario.TypeName}<{scenario.ConcreteType}>(";
        var expectedTypeRef = $"{scenario.TypeName}<{scenario.ConcreteType}>";
        return csContent.Contains(expectedNew) && csContent.Contains(expectedTypeRef);
    }

    /// <summary>
    /// Req 12.5: Emitter SHALL emit method invocations with type arguments.
    /// Verifies: .DoIt&lt;concrete&gt;( appears in emitted C#.
    /// </summary>
    private static bool VerifyMethodInvocation(string csContent, GenericEmissionScenario scenario)
    {
        var expected = $".DoIt<{scenario.ConcreteType}>(";
        return csContent.Contains(expected);
    }

    /// <summary>
    /// Req 12.6: Emitter SHALL emit generic inheritance clauses.
    /// Verifies: class Derived&lt;T&gt; : Base&lt;T&gt; appears in emitted C#.
    /// </summary>
    private static bool VerifyGenericInheritance(string csContent, GenericEmissionScenario scenario)
    {
        var expectedDerived = $"class {scenario.TypeName}<T>";
        var expectedBase = $": {scenario.BaseTypeName}<T>";
        return csContent.Contains(expectedDerived) && csContent.Contains(expectedBase);
    }

    /// <summary>
    /// Req 12.8: Emitter SHALL emit nested generic type arguments correctly.
    /// Verifies: TypeName&lt;TypeName&lt;concrete&gt;&gt; appears in emitted C#.
    /// </summary>
    private static bool VerifyNestedGenerics(string csContent, GenericEmissionScenario scenario)
    {
        var expected = $"{scenario.TypeName}<{scenario.TypeName}<{scenario.ConcreteType}>>";
        return csContent.Contains(expected);
    }
}
