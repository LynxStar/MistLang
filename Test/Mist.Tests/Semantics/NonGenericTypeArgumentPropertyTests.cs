// Feature: generics-type-arguments, Property 6: Non-generic type argument diagnostic
// **Validates: Requirements 7.3, 13.2**

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
/// Property-based tests verifying the MIST_NOT_GENERIC diagnostic.
///
/// Property 6: For any type reference that provides type arguments to a type that is not
/// a generic type declaration (i.e., has zero type parameters), the compiler SHALL emit
/// a MIST_NOT_GENERIC diagnostic error.
/// </summary>
public class NonGenericTypeArgumentPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

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

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = (typeDecls.ToImmutableArray()).AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario where type arguments are provided to a non-generic type.
    /// </summary>
    public enum NonGenericDeclKind { Class, Struct, Abstract }

    public record NonGenericTypeArgScenario(
        NonGenericDeclKind Kind,
        string NonGenericTypeName,
        string HolderTypeName,
        string FieldName,
        int ProvidedArgCount)
    {
        public override string ToString()
        {
            var kind = Kind.ToString().ToLowerInvariant();
            var args = string.Join(", ", Enumerable.Range(0, ProvidedArgCount).Select(_ => "int"));
            return $"{kind} {NonGenericTypeName} {{ }} class {HolderTypeName} {{ {NonGenericTypeName}<{args}> {FieldName}; }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] NonGenericNamePool = ["Simple", "Plain", "Basic"];
    private static readonly string[] HolderNamePool = ["Holder", "Owner", "Client"];
    private static readonly string[] FieldNamePool = ["field", "value", "data"];

    public static Gen<NonGenericTypeArgScenario> GenNonGenericTypeArgScenario()
    {
        return from kind in Gen.Elements(NonGenericDeclKind.Class, NonGenericDeclKind.Struct, NonGenericDeclKind.Abstract)
               from nonGenericName in Gen.Elements(NonGenericNamePool)
               from holderName in Gen.Elements(HolderNamePool)
               from fieldName in Gen.Elements(FieldNamePool)
               from argCount in Gen.Choose(1, 3)
               select new NonGenericTypeArgScenario(
                   kind, nonGenericName, holderName, fieldName, argCount);
    }

    public static Arbitrary<NonGenericTypeArgScenario> ArbitraryNonGenericTypeArgScenario() =>
        GenNonGenericTypeArgScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 6: For any type reference that provides type arguments to a non-generic type,
    /// the compiler emits MIST_NOT_GENERIC.
    ///
    /// **Validates: Requirements 7.3, 13.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NonGenericTypeArgumentPropertyTests) }, MaxTest = 100)]
    public bool NonGenericTypeWithArgs_EmitsNotGenericDiagnostic(NonGenericTypeArgScenario scenario)
    {
        // Build AST: non-generic type + holder class with a field using type args on it
        TypeDeclarationNode nonGenericTypeDecl;

        switch(scenario.Kind)
        {
            case NonGenericDeclKind.Class:
                {
                    var classDecl = new ClassDeclarationNode
                    {
                        Name = MakeToken(scenario.NonGenericTypeName),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                    };
                    nonGenericTypeDecl = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };
                    break;
                }
            case NonGenericDeclKind.Struct:
                {
                    var structDecl = new StructDeclarationNode
                    {
                        Name = MakeToken(scenario.NonGenericTypeName),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                    };
                    nonGenericTypeDecl = new StructTypeTypeDeclarationNode { StructDeclaration = structDecl };
                    break;
                }
            case NonGenericDeclKind.Abstract:
                {
                    var abstractDecl = new AbstractDeclarationNode
                    {
                        Name = MakeToken(scenario.NonGenericTypeName),
                        Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
                    };
                    nonGenericTypeDecl = new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl };
                    break;
                }
            default:
                throw new InvalidOperationException();
        }

        // Build type argument list
        var typeArgRefs = Enumerable.Range(0, scenario.ProvidedArgCount)
            .Select(_ => MakePrimitiveTypeRef("int"))
            .ToArray();
        var typeArgList = MakeTypeArgList(typeArgRefs);

        var fieldTypeRef = MakeNamedTypeRef(scenario.NonGenericTypeName, typeArgList);
        var fieldMember = MakeFieldMember(scenario.FieldName, fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken(scenario.HolderTypeName),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(nonGenericTypeDecl,
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should emit at least one MIST_NOT_GENERIC diagnostic
        var notGenericDiags = diagnostics
            .Where(d => d.Id == DiagnosticId.NotGeneric)
            .ToList();

        return notGenericDiags.Count >= 1;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void NonGenericClass_WithTypeArgs_EmitsNotGeneric()
    {
        // class Simple { }
        // class Holder { Simple<int> field; }
        var simpleClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Simple"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var fieldTypeRef = MakeNamedTypeRef("Simple", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = simpleClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var notGenericDiags = diagnostics.Where(d => d.Id == DiagnosticId.NotGeneric).ToList();
        Assert.Single(notGenericDiags);
        Assert.Contains("Simple", notGenericDiags[0].Message);
    }

    [Fact]
    public void NonGenericStruct_WithTypeArgs_EmitsNotGeneric()
    {
        // struct Point { }
        // class Holder { Point<int> field; }
        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Point"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var fieldTypeRef = MakeNamedTypeRef("Point", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new StructTypeTypeDeclarationNode { StructDeclaration = structDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var notGenericDiags = diagnostics.Where(d => d.Id == DiagnosticId.NotGeneric).ToList();
        Assert.Single(notGenericDiags);
        Assert.Contains("Point", notGenericDiags[0].Message);
    }

    [Fact]
    public void NonGenericAbstract_WithTypeArgs_EmitsNotGeneric()
    {
        // abstract ISimple { }
        // class Holder { ISimple<int> field; }
        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken("ISimple"),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int")]);
        var fieldTypeRef = MakeNamedTypeRef("ISimple", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var notGenericDiags = diagnostics.Where(d => d.Id == DiagnosticId.NotGeneric).ToList();
        Assert.Single(notGenericDiags);
        Assert.Contains("ISimple", notGenericDiags[0].Message);
    }

    [Fact]
    public void NonGenericClass_WithMultipleTypeArgs_EmitsNotGeneric()
    {
        // class Simple { }
        // class Holder { Simple<int, string> field; }
        var simpleClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Simple"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var typeArgList = MakeTypeArgList([MakePrimitiveTypeRef("int"), MakePrimitiveTypeRef("string")]);
        var fieldTypeRef = MakeNamedTypeRef("Simple", typeArgList);
        var fieldMember = MakeFieldMember("field", fieldTypeRef);

        var holderClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Holder"),
            Members = ImmutableArray.Create(fieldMember),
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = simpleClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = holderClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var notGenericDiags = diagnostics.Where(d => d.Id == DiagnosticId.NotGeneric).ToList();
        Assert.Single(notGenericDiags);
        Assert.Contains("Simple", notGenericDiags[0].Message);
    }
}