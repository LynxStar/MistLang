// Feature: void-unit-type, Property 1: Void type reference resolution
// **Validates: Requirements 1.4, 1.5, 3.1, 4.1, 5.1, 6.1**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying void type reference resolution in the semantic model.
///
/// Property 1: For any Mist program containing a type reference to `void` (keyword) or
/// `Void` (identifier) in any valid position (return type, variable type, parameter type,
/// generic type argument), the semantic model builder SHALL resolve the type reference to
/// the void type (identified via <c>IsVoid()</c>).
/// </summary>
public class VoidTypeResolutionPropertyTests
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

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
    }

    private static TypeReferenceNode MakeVoidKeywordTypeRef()
    {
        return new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() };
    }

    private static TypeReferenceNode MakeVoidIdentifierTypeRef()
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken("Void") },
            }
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
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name, TypeArgumentListNode? typeArgs = null)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(name) },
                TypeArgs = typeArgs,
            }
        };
    }

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

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
    }

    private static MemberDeclarationNode MakeFieldMember(string name, TypeReferenceNode typeRef)
    {
        return new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = MakeToken(name),
                Type = typeRef,
            },
        };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a void type reference variant: either the `void` keyword or the `Void` identifier.
    /// </summary>
    public enum VoidRefKind
    {
        /// <summary>The `void` keyword type reference.</summary>
        Keyword,
        /// <summary>The `Void` identifier type reference.</summary>
        Identifier,
    }

    /// <summary>
    /// Represents the position where a void type reference appears.
    /// </summary>
    public enum VoidPosition
    {
        ReturnType,
        VariableType,
        ParameterType,
        GenericTypeArgument,
    }

    /// <summary>
    /// A scenario combining a void reference kind and a position.
    /// </summary>
    public record VoidTypeRefScenario(
        VoidRefKind RefKind,
        VoidPosition Position,
        string MethodName,
        string VarName,
        string ParamName,
        string GenericTypeName)
    {
        public override string ToString()
        {
            var voidStr = RefKind == VoidRefKind.Keyword ? "void" : "Void";
            return $"{voidStr} in {Position} (method={MethodName}, var={VarName}, param={ParamName}, generic={GenericTypeName})";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] MethodNames = ["DoWork", "Process", "Execute", "Run", "Handle"];
    private static readonly string[] VarNames = ["x", "y", "result", "value", "temp"];
    private static readonly string[] ParamNames = ["arg", "input", "param", "data", "item"];
    private static readonly string[] GenericTypeNames = ["Box", "Container", "Wrapper", "Result", "Option"];

    public static Gen<VoidTypeRefScenario> GenScenario()
    {
        return from refKind in Gen.Elements(VoidRefKind.Keyword, VoidRefKind.Identifier)
               from position in Gen.Elements(
                   VoidPosition.ReturnType,
                   VoidPosition.VariableType,
                   VoidPosition.ParameterType,
                   VoidPosition.GenericTypeArgument)
               from methodName in Gen.Elements(MethodNames)
               from varName in Gen.Elements(VarNames)
               from paramName in Gen.Elements(ParamNames)
               from genericTypeName in Gen.Elements(GenericTypeNames)
               select new VoidTypeRefScenario(refKind, position, methodName, varName, paramName, genericTypeName);
    }

    public static Arbitrary<VoidTypeRefScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Scenario builders ───────────────────────────────────────────────

    private static TypeReferenceNode MakeVoidTypeRef(VoidRefKind kind)
    {
        return kind switch
        {
            VoidRefKind.Keyword => MakeVoidKeywordTypeRef(),
            VoidRefKind.Identifier => MakeVoidIdentifierTypeRef(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>
    /// Builds a program with void as a method return type and verifies the method's
    /// declared return type resolves to the void type.
    /// </summary>
    private bool VerifyReturnType(VoidTypeRefScenario scenario)
    {
        var voidTypeRef = MakeVoidTypeRef(scenario.RefKind);

        // Build: class Program { public static void Main() { } public <void> MethodName() { } }
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var testMethod = new MethodDeclarationNode
        {
            Name = MakeToken(scenario.MethodName),
            ReturnType = voidTypeRef,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod },
                new MethodMemberMemberDeclarationNode { Method = testMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        // Find the test method symbol and check its return type
        var methodSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.MethodName && s.Kind == SymbolKind.Method);
        if(methodSymbol is null)
        {
            return false;
        }

        return methodSymbol.DeclaredType?.IsVoid() == true;
    }

    /// <summary>
    /// Builds a program with void as a variable type and verifies the variable's
    /// declared type resolves to the void type.
    /// </summary>
    private bool VerifyVariableType(VoidTypeRefScenario scenario)
    {
        // For variable type, only the `void` keyword works as a type reference
        // because `Void` as an identifier in a typed variable declaration is a
        // NamedTypeRef which resolves through TryResolveWellKnownType.
        var voidTypeRef = MakeVoidTypeRef(scenario.RefKind);

        // Build: class Program { public static void Main() { <void> varName = ...; } }
        // We need a valid initializer for a void variable. Use a simple expression
        // that won't cause issues — the semantic model builder resolves the declared type
        // independently of the initializer.
        var voidVarDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = voidTypeRef,
                Name = MakeToken(scenario.VarName),
                Initializer = MistAstGenerator.WrapPrimary(
                    new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = MakeToken("0") }),
            },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(voidVarDecl) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        // Find the variable symbol and check its declared type
        var varSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.VarName && s.Kind == SymbolKind.LocalVariable);
        if(varSymbol is null)
        {
            return false;
        }

        return varSymbol.DeclaredType?.IsVoid() == true;
    }

    /// <summary>
    /// Builds a program with void as a parameter type and verifies the parameter's
    /// declared type resolves to the void type.
    /// </summary>
    private bool VerifyParameterType(VoidTypeRefScenario scenario)
    {
        var voidTypeRef = MakeVoidTypeRef(scenario.RefKind);

        // Build: class Program { public static void Main() { } public void MethodName(<void> paramName) { } }
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var paramList = new ParameterListNode
        {
            Parameter = new ParameterNode
            {
                Type = voidTypeRef,
                Name = MakeToken(scenario.ParamName),
            },
        };

        var testMethod = new MethodDeclarationNode
        {
            Name = MakeToken(scenario.MethodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod },
                new MethodMemberMemberDeclarationNode { Method = testMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        // Find the parameter symbol and check its declared type
        var paramSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ParamName && s.Kind == SymbolKind.Parameter);
        if(paramSymbol is null)
        {
            return false;
        }

        return paramSymbol.DeclaredType?.IsVoid() == true;
    }

    /// <summary>
    /// Builds a program with void as a generic type argument and verifies the type argument
    /// resolves to the void type within the constructed type.
    /// </summary>
    private bool VerifyGenericTypeArgument(VoidTypeRefScenario scenario)
    {
        var voidTypeRef = MakeVoidTypeRef(scenario.RefKind);

        // Build: class Box<T> { } class Program { Box<void> field; public static void Main() { } }
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([voidTypeRef]);
        var fieldTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, typeArgList);
        var fieldMember = MakeFieldMember("voidField", fieldTypeRef);

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create(
                fieldMember,
                new MethodMemberMemberDeclarationNode { Method = mainMethod } as MemberDeclarationNode),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = programClassDecl });

        var (model, _) = BuildModel(ast);

        // Find the field symbol and check its type argument
        var fieldSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "voidField" && s.Kind == SymbolKind.Field);
        if(fieldSymbol is null)
        {
            return false;
        }

        if(fieldSymbol.DeclaredType is not ConstructedType constructedType)
        {
            return false;
        }

        if(constructedType.TypeArguments.Count != 1)
        {
            return false;
        }

        return constructedType.TypeArguments[0].IsVoid();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 1: For any Mist program containing a type reference to `void` or `Void`
    /// in any valid position (return type, variable type, parameter type, generic type argument),
    /// the semantic model builder resolves the type reference to the void type.
    ///
    /// **Validates: Requirements 1.4, 1.5, 3.1, 4.1, 5.1, 6.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VoidTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool VoidTypeRef_ResolvesToVoidTypeInstance(VoidTypeRefScenario scenario)
    {
        return scenario.Position switch
        {
            VoidPosition.ReturnType => VerifyReturnType(scenario),
            VoidPosition.VariableType => VerifyVariableType(scenario),
            VoidPosition.ParameterType => VerifyParameterType(scenario),
            VoidPosition.GenericTypeArgument => VerifyGenericTypeArgument(scenario),
            _ => false,
        };
    }
}