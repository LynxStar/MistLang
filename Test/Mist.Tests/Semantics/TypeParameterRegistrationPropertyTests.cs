// Feature: generics-type-arguments, Property 2: Type parameter registration
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 9.1, 9.2**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying type parameter registration in the semantic model.
///
/// Property 2: For any generic type declaration (class, struct, or abstract type) with N type
/// parameters, and for any generic method declaration with M type parameters, the semantic
/// model builder SHALL create exactly N (or M) TypeParameterType instances, store them on
/// the declaring type's (or method symbol's) TypeParameters property with correct ordinals
/// (0 through N-1), and register each as a resolvable type in the declaring scope.
/// </summary>
public class TypeParameterRegistrationPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

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

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(string[] names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = MakeToken(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = MakeToken(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static ClassDeclarationNode MakeGenericClass(string name, string[] typeParamNames)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            TypeParams = MakeTypeParamList(typeParamNames),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static StructDeclarationNode MakeGenericStruct(string name, string[] typeParamNames)
    {
        return new StructDeclarationNode
        {
            Name = MakeToken(name),
            TypeParams = MakeTypeParamList(typeParamNames),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static AbstractDeclarationNode MakeGenericAbstract(string name, string[] typeParamNames)
    {
        return new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            TypeParams = MakeTypeParamList(typeParamNames),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };
    }

    private static MethodDeclarationNode MakeGenericMethod(string name, string[] typeParamNames)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            TypeParams = MakeTypeParamList(typeParamNames),
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a generic type declaration for testing type parameter registration.
    /// </summary>
#pragma warning disable CA1034
    public enum GenericDeclKind { Class, Struct, Abstract }

    public record GenericTypeDecl(string Name, GenericDeclKind Kind, ImmutableArray<string> TypeParamNames)
    {
        public override string ToString()
        {
            var kind = Kind.ToString().ToLowerInvariant();
            return $"{kind} {Name}<{string.Join(", ", TypeParamNames)}>";
        }
    }

    /// <summary>
    /// Represents a generic method inside a class for testing method type parameter registration.
    /// </summary>
    public record GenericMethodDecl(string ClassName, string MethodName, ImmutableArray<string> TypeParamNames)
    {
        public override string ToString()
        {
            return $"class {ClassName} {{ void {MethodName}<{string.Join(", ", TypeParamNames)}>() {{ }} }}";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] TypeNamePool = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
    private static readonly string[] MethodNamePool = ["DoWork", "Process", "Execute", "Transform"];

    public static Gen<GenericTypeDecl> GenGenericTypeDecl()
    {
        return from name in Gen.Elements(TypeNamePool)
               from kind in Gen.Elements(GenericDeclKind.Class, GenericDeclKind.Struct, GenericDeclKind.Abstract)
               from count in Gen.Choose(1, 4)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               select new GenericTypeDecl(name, kind, typeParams);
    }

    public static Gen<GenericMethodDecl> GenGenericMethodDecl()
    {
        return from className in Gen.Elements(TypeNamePool)
               from methodName in Gen.Elements(MethodNamePool)
               from count in Gen.Choose(1, 4)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               select new GenericMethodDecl(className, methodName, typeParams);
    }

    public static Arbitrary<GenericTypeDecl> ArbitraryGenericTypeDecl()
    {
        return GenGenericTypeDecl().ToArbitrary();
    }

    public static Arbitrary<GenericMethodDecl> ArbitraryGenericMethodDecl()
    {
        return GenGenericMethodDecl().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: For any generic type declaration with N type parameters,
    /// the semantic model builder creates exactly N TypeParameterType instances
    /// with correct ordinals (0 through N-1), stores them on the declaring type's
    /// TypeParameters property, and registers each in the declaring scope.
    ///
    /// **Validates: Requirements 6.1, 6.3, 6.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeParameterRegistrationPropertyTests) }, MaxTest = 100)]
    public bool GenericType_TypeParametersCorrectlyRegistered(GenericTypeDecl decl)
    {
        // Build AST
        TypeDeclarationNode typeDecl = decl.Kind switch
        {
            GenericDeclKind.Class => new ClassTypeTypeDeclarationNode
            {
                ClassDeclaration = MakeGenericClass(decl.Name, decl.TypeParamNames.ToArray()),
            },
            GenericDeclKind.Struct => new StructTypeTypeDeclarationNode
            {
                StructDeclaration = MakeGenericStruct(decl.Name, decl.TypeParamNames.ToArray()),
            },
            GenericDeclKind.Abstract => new AbstractTypeTypeDeclarationNode
            {
                AbstractDeclaration = MakeGenericAbstract(decl.Name, decl.TypeParamNames.ToArray()),
            },
            _ => throw new InvalidOperationException(),
        };

        var ast = MakeCompilationUnit(typeDecl);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Find the type symbol
        var expectedKind = decl.Kind switch
        {
            GenericDeclKind.Class => SymbolKind.Class,
            GenericDeclKind.Struct => SymbolKind.Struct,
            GenericDeclKind.Abstract => SymbolKind.Abstract,
            _ => throw new InvalidOperationException(),
        };

        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == decl.Name && s.Kind == expectedKind);
        if(typeSymbol is null)
        {
            return false;
        }

        // Get the TypeParameters from the declaring type
        var typeParams = decl.Kind switch
        {
            GenericDeclKind.Class => ((ClassType)typeSymbol.DeclaredType!).TypeParameters,
            GenericDeclKind.Struct => ((StructType)typeSymbol.DeclaredType!).TypeParameters,
            GenericDeclKind.Abstract => ((AbstractType)typeSymbol.DeclaredType!).TypeParameters,
            _ => throw new InvalidOperationException(),
        };

        // Requirement 6.3: Exactly N TypeParameterType instances
        if(typeParams.Count != decl.TypeParamNames.Length)
        {
            return false;
        }

        // Verify correct names and ordinals (0 through N-1)
        for(var i = 0; i < typeParams.Count; i++)
        {
            if(typeParams[i] is not TypeParameterType tpCheck)
            {
                return false;
            }

            if(tpCheck.Name != decl.TypeParamNames[i])
            {
                return false;
            }

            if(tpCheck.Ordinal != i)
            {
                return false;
            }

            if(tpCheck.DisplayName != decl.TypeParamNames[i])
            {
                return false;
            }
        }

        // Requirement 6.4: Each type parameter is registered in the declaring scope
        // and resolvable as a type
        var memberScope = decl.Kind switch
        {
            GenericDeclKind.Class => ((ClassType)typeSymbol.DeclaredType!).MemberScope,
            GenericDeclKind.Struct => ((StructType)typeSymbol.DeclaredType!).MemberScope,
            GenericDeclKind.Abstract => ((AbstractType)typeSymbol.DeclaredType!).MemberScope,
            _ => throw new InvalidOperationException(),
        };

        foreach(var tp in typeParams)
        {
            if(tp is not TypeParameterType tpCast2)
            {
                return false;
            }

            var tpSymbol = memberScope.Members
                .FirstOrDefault(m => m.Name == tpCast2.Name && m.DeclaredType is TypeParameterType);
            if(tpSymbol is null)
            {
                return false;
            }

            // The symbol's DeclaredType should be the TypeParameterType
            if(tpSymbol.DeclaredType is not TypeParameterType resolvedTp)
            {
                return false;
            }

            if(resolvedTp.Ordinal != tpCast2.Ordinal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 2b: For any generic method declaration with M type parameters,
    /// the semantic model builder creates exactly M TypeParameterType instances
    /// with correct ordinals, stores them on the method symbol's TypeParameters
    /// property, and registers each in the declaring scope.
    ///
    /// **Validates: Requirements 6.2, 9.1, 9.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeParameterRegistrationPropertyTests) }, MaxTest = 100)]
    public bool GenericMethod_TypeParametersCorrectlyRegistered(GenericMethodDecl decl)
    {
        // Build AST: a class containing a generic method
        var method = MakeGenericMethod(decl.MethodName, decl.TypeParamNames.ToArray());
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(decl.ClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Find the method symbol
        var methodSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == decl.MethodName && s.Kind == SymbolKind.Method);
        if(methodSymbol is null)
        {
            return false;
        }

        // Get TypeParameters from the method symbol
        var methodSym = (Symbol)methodSymbol;
        var typeParams = methodSym.TypeParameters;

        // Exactly M TypeParameterType instances
        if(typeParams.Count != decl.TypeParamNames.Length)
        {
            return false;
        }

        // Verify correct names and ordinals
        for(var i = 0; i < typeParams.Count; i++)
        {
            if(typeParams[i].DisplayName != decl.TypeParamNames[i])
            {
                return false;
            }

            if(typeParams[i] is TypeParameterType tpOrd && tpOrd.Ordinal != i)
            {
                return false;
            }
        }

        // Each type parameter should be registered in the type scope
        // (method type params are registered in the enclosing type scope)
        var classSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == decl.ClassName && s.Kind == SymbolKind.Class);
        if(classSymbol is null)
        {
            return false;
        }

        var classType = (ClassType)classSymbol.DeclaredType!;
        var memberScope = classType.MemberScope;

        foreach(var tp in typeParams)
        {
            var tpSymbol = memberScope.Members
                .FirstOrDefault(m => m.Name == tp.DisplayName && m.DeclaredType is TypeParameterType);
            if(tpSymbol is null)
            {
                return false;
            }
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void SingleTypeParameter_Class_RegisteredCorrectly()
    {
        var classDecl = MakeGenericClass("Box", ["T"]);
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.First(s => s.Name == "Box" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(symbol.DeclaredType);

        _ = Assert.Single(classType.TypeParameters);
        Assert.Equal("T", classType.TypeParameters[0].DisplayName);
        Assert.Equal(0, ((TypeParameterType)classType.TypeParameters[0]).Ordinal);
    }

    [Fact]
    public void MultipleTypeParameters_Class_RegisteredWithCorrectOrdinals()
    {
        var classDecl = MakeGenericClass("Map", ["K", "V"]);
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.First(s => s.Name == "Map" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(symbol.DeclaredType);

        Assert.Equal(2, classType.TypeParameters.Count);
        Assert.Equal("K", classType.TypeParameters[0].DisplayName);
        Assert.Equal(0, ((TypeParameterType)classType.TypeParameters[0]).Ordinal);
        Assert.Equal("V", classType.TypeParameters[1].DisplayName);
        Assert.Equal(1, ((TypeParameterType)classType.TypeParameters[1]).Ordinal);
    }

    [Fact]
    public void GenericStruct_TypeParametersRegistered()
    {
        var structDecl = MakeGenericStruct("Pair", ["T", "U"]);
        var ast = MakeCompilationUnit(
            new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.First(s => s.Name == "Pair" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(symbol.DeclaredType);

        Assert.Equal(2, structType.TypeParameters.Count);
        Assert.Equal("T", structType.TypeParameters[0].DisplayName);
        Assert.Equal("U", structType.TypeParameters[1].DisplayName);
    }

    [Fact]
    public void GenericAbstract_TypeParametersRegistered()
    {
        var abstractDecl = MakeGenericAbstract("IContainer", ["T"]);
        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl });
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.First(s => s.Name == "IContainer" && s.Kind == SymbolKind.Abstract);
        var abstractType = Assert.IsType<AbstractType>(symbol.DeclaredType);

        _ = Assert.Single(abstractType.TypeParameters);
        Assert.Equal("T", abstractType.TypeParameters[0].DisplayName);
        Assert.Equal(0, ((TypeParameterType)abstractType.TypeParameters[0]).Ordinal);
    }

    [Fact]
    public void GenericMethod_TypeParametersStoredOnSymbol()
    {
        var method = MakeGenericMethod("Identity", ["T"]);
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyClass"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };
        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModelWithDiagnostics(ast);

        var methodSymbol = (Symbol)model.AllSymbols.First(s => s.Name == "Identity" && s.Kind == SymbolKind.Method);

        _ = Assert.Single(methodSymbol.TypeParameters);
        Assert.Equal("T", methodSymbol.TypeParameters[0].DisplayName);
        Assert.Equal(0, ((TypeParameterType)methodSymbol.TypeParameters[0]).Ordinal);
    }
}