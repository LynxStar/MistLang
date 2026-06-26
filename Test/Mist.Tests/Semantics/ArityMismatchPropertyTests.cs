// Feature: generics-type-arguments, Property 5: Arity mismatch diagnostic
// **Validates: Requirements 7.2, 11.6, 13.1, 13.4**

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
/// Property-based tests verifying arity mismatch diagnostics in the semantic model.
///
/// Property 5: For any type reference or method invocation that provides a different number
/// of type arguments than the referenced generic type or method declares type parameters,
/// the compiler SHALL emit a diagnostic error stating the expected and actual arity.
/// This applies to both type-level arity mismatches (MIST_ARITY_MISMATCH) and method-level
/// arity mismatches (MIST_METHOD_ARITY_MISMATCH).
/// </summary>
public class ArityMismatchPropertyTests
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

    private static TypeArgumentListNode MakeTypeArgList(TypeReferenceNode[] typeRefs)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = typeRefs[0] },
            TypeArguments = typeRefs.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
        };
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

    /// <summary>
    /// Wraps a PostfixNode into a full ExpressionNode through the expression chain.
    /// </summary>
    private static ExpressionNode WrapPostfix(PostfixNode postfix)
    {
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

    /// <summary>
    /// Creates an expression statement: this.methodName&lt;typeArgs&gt;()
    /// </summary>
    private static StatementNode MakeMethodCallStatement(string methodName, TypeArgumentListNode typeArgs)
    {
        var primary = new ThisExpressionPrimaryNode();
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = MakeToken(methodName),
            TypeArgs = typeArgs,
            Arguments = null,
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var expr = WrapPostfix(postfix);
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: A type reference provides a wrong number of type arguments to a generic type.
    /// </summary>
    public record TypeArityMismatchScenario(
        string GenericTypeName,
        string HolderTypeName,
        ImmutableArray<string> TypeParamNames,
        int ProvidedArgCount)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", TypeParamNames);
            var args = string.Join(", ", Enumerable.Range(0, ProvidedArgCount).Select(_ => "int"));
            return $"class {GenericTypeName}<{tps}> {{ }} class {HolderTypeName} {{ {GenericTypeName}<{args}> field; }}";
        }
    }

    /// <summary>
    /// Scenario: A method invocation provides a wrong number of type arguments to a generic method.
    /// </summary>
    public record MethodArityMismatchScenario(
        string ClassName,
        string MethodName,
        ImmutableArray<string> MethodTypeParamNames,
        int ProvidedArgCount)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", MethodTypeParamNames);
            var args = string.Join(", ", Enumerable.Range(0, ProvidedArgCount).Select(_ => "int"));
            return $"class {ClassName} {{ void {MethodName}<{tps}>() {{ }} void Caller() {{ this.{MethodName}<{args}>(); }} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] GenericNamePool = ["Box", "Container", "Wrapper"];
    private static readonly string[] HolderNamePool = ["Holder", "Owner", "Client"];
    private static readonly string[] MethodNamePool = ["DoWork", "Process", "Execute"];
    private static readonly string[] ClassNamePool = ["Alpha", "Beta", "Gamma"];

    public static Gen<TypeArityMismatchScenario> GenTypeArityMismatchScenario()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from holderName in Gen.Elements(HolderNamePool)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(paramCount).ToImmutableArray()
               from providedCount in Gen.Choose(1, 4).Where(c => c != paramCount)
               select new TypeArityMismatchScenario(
                   genericName, holderName, typeParams, providedCount);
    }

    public static Gen<MethodArityMismatchScenario> GenMethodArityMismatchScenario()
    {
        return from className in Gen.Elements(ClassNamePool)
               from methodName in Gen.Elements(MethodNamePool)
               from paramCount in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(paramCount).ToImmutableArray()
               from providedCount in Gen.Choose(1, 4).Where(c => c != paramCount)
               select new MethodArityMismatchScenario(
                   className, methodName, typeParams, providedCount);
    }

    public static Arbitrary<TypeArityMismatchScenario> ArbitraryTypeArityMismatchScenario()
    {
        return GenTypeArityMismatchScenario().ToArbitrary();
    }

    public static Arbitrary<MethodArityMismatchScenario> ArbitraryMethodArityMismatchScenario()
    {
        return GenMethodArityMismatchScenario().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5a: For any type reference that provides a different number of type arguments
    /// than the referenced generic type declares type parameters, the compiler emits
    /// MIST_ARITY_MISMATCH.
    ///
    /// **Validates: Requirements 7.2, 13.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ArityMismatchPropertyTests) }, MaxTest = 100)]
    public bool TypeArityMismatch_EmitsDiagnostic(TypeArityMismatchScenario scenario)
    {
        // Build AST: generic class + holder class with a field using wrong arity
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build type argument list with wrong number of args
        var typeArgRefs = Enumerable.Range(0, scenario.ProvidedArgCount)
            .Select(_ => MakePrimitiveTypeRef("int"))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        var fieldTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.HolderTypeName),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should emit at least one MIST_ARITY_MISMATCH diagnostic
        var arityDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.ArityMismatch)
            .ToList();

        return arityDiags.Count >= 1;
    }

    /// <summary>
    /// Property 5b: For any method invocation that provides a different number of type
    /// arguments than the method declares type parameters, the compiler emits
    /// MIST_METHOD_ARITY_MISMATCH.
    ///
    /// **Validates: Requirements 11.6, 13.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ArityMismatchPropertyTests) }, MaxTest = 100)]
    public bool MethodArityMismatch_EmitsDiagnostic(MethodArityMismatchScenario scenario)
    {
        // Build AST: class with a generic method + a caller method that invokes it with wrong arity
        var genericMethod = MakeGenericMethod(scenario.MethodName, scenario.MethodTypeParamNames.ToArray());

        // Build type argument list with wrong number of args for the method call
        var typeArgRefs = Enumerable.Range(0, scenario.ProvidedArgCount)
            .Select(_ => MakePrimitiveTypeRef("int"))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        // Create a caller method that invokes the generic method with wrong arity
        var callStatement = MakeMethodCallStatement(scenario.MethodName, typeArgList);
        var callerMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Caller"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create(callStatement) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.ClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = genericMethod },
                new MethodMemberMemberDeclarationNode { Method = callerMethod }),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should emit at least one MIST_METHOD_ARITY_MISMATCH diagnostic
        var arityDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.MethodArityMismatch)
            .ToList();

        return arityDiags.Count >= 1;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void TypeRef_TooFewArgs_EmitsArityMismatch()
    {
        // class Map<K, V> { }
        // class Holder { Map<int> field; }  ← 1 arg, expects 2
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var fieldTypeRef = MakeNamedTypeRef("Map", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var arityDiags = diagnostics.Where(d => d.Id == DiagnosticId.ArityMismatch).ToList();
        _ = Assert.Single(arityDiags);
        Assert.Contains("Map", arityDiags[0].Message);
    }

    [Fact]
    public void TypeRef_TooManyArgs_EmitsArityMismatch()
    {
        // class Box<T> { }
        // class Holder { Box<int, string> field; }  ← 2 args, expects 1
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int"), MakePrimitiveTypeRef("string")]);
        var fieldTypeRef = MakeNamedTypeRef("Box", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var arityDiags = diagnostics.Where(d => d.Id == DiagnosticId.ArityMismatch).ToList();
        _ = Assert.Single(arityDiags);
        Assert.Contains("Box", arityDiags[0].Message);
    }

    [Fact]
    public void MethodCall_WrongArity_EmitsMethodArityMismatch()
    {
        // class C { void DoWork<T>() { } void Caller() { this.DoWork<int, string>(); } }
        var genericMethod = MakeGenericMethod("DoWork", ["T"]);

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int"), MakePrimitiveTypeRef("string")]);
        var callStatement = MakeMethodCallStatement("DoWork", typeArgList);

        var callerMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Caller"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create(callStatement) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = genericMethod },
                new MethodMemberMemberDeclarationNode { Method = callerMethod }),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var arityDiags = diagnostics.Where(d => d.Id == DiagnosticId.MethodArityMismatch).ToList();
        _ = Assert.Single(arityDiags);
        Assert.Contains("DoWork", arityDiags[0].Message);
    }
}