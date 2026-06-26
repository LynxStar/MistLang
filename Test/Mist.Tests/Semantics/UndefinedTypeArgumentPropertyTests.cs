// Feature: generics-type-arguments, Property 13: Undefined type argument diagnostic
// **Validates: Requirements 13.6**

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
/// Property-based tests verifying the MIST_UNDEFINED_TYPE_ARGUMENT diagnostic.
///
/// Property 13: For any type reference where a type argument name cannot be resolved to
/// a known type (neither a declared type, a type parameter in scope, nor a primitive type),
/// the compiler SHALL emit a MIST_UNDEFINED_TYPE_ARGUMENT diagnostic error.
/// </summary>
public class UndefinedTypeArgumentPropertyTests
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

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: A type reference provides an undefined type name as a type argument
    /// to a generic type.
    /// e.g., class Box&lt;T&gt; { } class Holder { Box&lt;Nonexistent&gt; field; }
    /// </summary>
    public record UndefinedTypeArgScenario(
        string GenericTypeName,
        string HolderTypeName,
        string FieldName,
        ImmutableArray<string> TypeParamNames,
        ImmutableArray<string> UndefinedArgNames)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", TypeParamNames);
            var args = string.Join(", ", UndefinedArgNames);
            return $"class {GenericTypeName}<{tps}> {{ }} class {HolderTypeName} {{ {GenericTypeName}<{args}> {FieldName}; }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] GenericNamePool = ["Box", "Container", "Wrapper"];
    private static readonly string[] HolderNamePool = ["Holder", "Owner", "Client"];
    private static readonly string[] FieldNamePool = ["field", "value", "data"];
    private static readonly string[] UndefinedNamePool = ["Nonexistent", "Missing", "Unknown", "Bogus"];

    public static Gen<UndefinedTypeArgScenario> GenUndefinedTypeArgScenario()
    {
        return from genericName in Gen.Elements(GenericNamePool)
               from holderName in Gen.Elements(HolderNamePool)
               from fieldName in Gen.Elements(FieldNamePool)
               from count in Gen.Choose(1, 3)
               let typeParams = TypeParamPool.Take(count).ToImmutableArray()
               from undefinedArgs in Gen.ArrayOf(Gen.Elements(UndefinedNamePool), count)
               select new UndefinedTypeArgScenario(
                   genericName, holderName, fieldName, typeParams,
                   undefinedArgs.ToImmutableArray());
    }

    public static Arbitrary<UndefinedTypeArgScenario> ArbitraryUndefinedTypeArgScenario() =>
        GenUndefinedTypeArgScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 13: For any type reference where a type argument name cannot be resolved
    /// to a known type, the compiler emits MIST_UNDEFINED_TYPE_ARGUMENT.
    ///
    /// **Validates: Requirements 13.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UndefinedTypeArgumentPropertyTests) }, MaxTest = 100)]
    public bool UndefinedTypeArg_EmitsDiagnostic(UndefinedTypeArgScenario scenario)
    {
        // Build AST: generic class + holder class with a field using undefined type args
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.GenericTypeName),
            TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // Build type argument list with undefined type names
        var typeArgRefs = scenario.UndefinedArgNames
            .Select(name => MakeNamedTypeRef(name))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        var fieldTypeRef = MakeNamedTypeRef(scenario.GenericTypeName, typeArgList);
        var fieldMember = MakeFieldMember(scenario.FieldName, fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.HolderTypeName),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should emit at least one MIST_UNDEFINED_TYPE_ARGUMENT diagnostic
        var undefinedDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.UndefinedTypeArgument)
            .ToList();

        return undefinedDiags.Count >= 1;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void SingleUndefinedTypeArg_EmitsUndefinedTypeArgDiagnostic()
    {
        // class Box<T> { }
        // class Holder { Box<Nonexistent> field; }
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakeNamedTypeRef("Nonexistent")]);
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

        var undefinedDiags = diagnostics.Where(d => d.Id == DiagnosticId.UndefinedTypeArgument).ToList();
        _ = Assert.Single(undefinedDiags);
        Assert.Contains("Nonexistent", undefinedDiags[0].Message);
    }

    [Fact]
    public void MultipleUndefinedTypeArgs_EmitsMultipleDiagnostics()
    {
        // class Map<K, V> { }
        // class Holder { Map<Missing, Unknown> field; }
        var genericClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakeNamedTypeRef("Missing"), MakeNamedTypeRef("Unknown")]);
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

        var undefinedDiags = diagnostics.Where(d => d.Id == DiagnosticId.UndefinedTypeArgument).ToList();
        Assert.Equal(2, undefinedDiags.Count);
    }

    [Fact]
    public void MixedDefinedAndUndefined_EmitsDiagnosticForUndefinedOnly()
    {
        // class Box<T> { }
        // class Map<K, V> { }
        // class Holder { Map<int, Nonexistent> field; }
        var boxClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Box"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var mapClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            MakeNamedTypeRef("Nonexistent"),
        ]);
        var fieldTypeRef = MakeNamedTypeRef("Map", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = boxClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = mapClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var undefinedDiags = diagnostics.Where(d => d.Id == DiagnosticId.UndefinedTypeArgument).ToList();
        _ = Assert.Single(undefinedDiags);
        Assert.Contains("Nonexistent", undefinedDiags[0].Message);
    }
}