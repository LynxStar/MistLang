// Feature: generics-type-arguments, Property 8: Missing type arguments diagnostic
// **Validates: Requirements 8.4, 13.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying the MIST_MISSING_TYPE_ARGUMENTS diagnostic.
///
/// Property 8: For any non-generic type that inherits from a generic type without
/// providing type arguments, the compiler SHALL emit a MIST_MISSING_TYPE_ARGUMENTS
/// diagnostic error.
/// </summary>
public class MissingTypeArgumentsPropertyTests
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

    private static BaseListNode MakeBaseList(BaseTypeRefNode[] entries)
    {
        return new BaseListNode
        {
            First = entries[0],
            Rest = entries[0],
            BaseTypeRefs = entries.Skip(1).ToImmutableArray(),
        };
    }

    private static BaseTypeRefNode MakeBaseTypeRef(string name)
    {
        return new BaseTypeRefNode { Name = MakeToken(name) };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario where a non-generic type inherits from a generic type
    /// without providing type arguments.
    /// </summary>
    public enum MissingArgsDeclKind { ClassFromClass, ClassFromAbstract, StructFromAbstract, AbstractFromAbstract }

    public record MissingTypeArgsScenario(
        MissingArgsDeclKind Kind,
        string GenericTypeName,
        string DerivedTypeName,
        ImmutableArray<string> TypeParamNames)
    {
        public override string ToString()
        {
            var tps = string.Join(", ", TypeParamNames);
            return Kind switch
            {
                MissingArgsDeclKind.ClassFromClass =>
                    $"class {GenericTypeName}<{tps}> {{ }} class {DerivedTypeName} : {GenericTypeName} {{ }}",
                MissingArgsDeclKind.ClassFromAbstract =>
                    $"abstract {GenericTypeName}<{tps}> {{ }} class {DerivedTypeName} : {GenericTypeName} {{ }}",
                MissingArgsDeclKind.StructFromAbstract =>
                    $"abstract {GenericTypeName}<{tps}> {{ }} struct {DerivedTypeName} : {GenericTypeName} {{ }}",
                MissingArgsDeclKind.AbstractFromAbstract =>
                    $"abstract {GenericTypeName}<{tps}> {{ }} abstract {DerivedTypeName} : {GenericTypeName} {{ }}",
                _ => "unknown",
            };
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamPool = ["T", "U", "V", "W"];
    private static readonly string[] GenericNamePool = ["Base", "Parent", "Root"];
    private static readonly string[] DerivedNamePool = ["Derived", "Child", "Sub"];
    private static readonly string[] AbstractGenericNamePool = ["IContainer", "ICollection", "IReadable"];
    private static readonly string[] AbstractDerivedNamePool = ["IChild", "ISub", "IExtended"];

    public static Gen<MissingTypeArgsScenario> GenMissingTypeArgsScenario()
    {
        var genClassFromClass =
            from genericName in Gen.Elements(GenericNamePool)
            from derivedName in Gen.Elements(DerivedNamePool)
            from count in Gen.Choose(1, 3)
            let typeParams = TypeParamPool.Take(count).ToImmutableArray()
            select new MissingTypeArgsScenario(
                MissingArgsDeclKind.ClassFromClass, genericName, derivedName, typeParams);

        var genClassFromAbstract =
            from genericName in Gen.Elements(AbstractGenericNamePool)
            from derivedName in Gen.Elements(DerivedNamePool)
            from count in Gen.Choose(1, 3)
            let typeParams = TypeParamPool.Take(count).ToImmutableArray()
            select new MissingTypeArgsScenario(
                MissingArgsDeclKind.ClassFromAbstract, genericName, derivedName, typeParams);

        var genStructFromAbstract =
            from genericName in Gen.Elements(AbstractGenericNamePool)
            from derivedName in Gen.Elements(DerivedNamePool)
            from count in Gen.Choose(1, 3)
            let typeParams = TypeParamPool.Take(count).ToImmutableArray()
            select new MissingTypeArgsScenario(
                MissingArgsDeclKind.StructFromAbstract, genericName, derivedName, typeParams);

        var genAbstractFromAbstract =
            from genericName in Gen.Elements(AbstractGenericNamePool)
            from derivedName in Gen.Elements(AbstractDerivedNamePool)
            from count in Gen.Choose(1, 3)
            let typeParams = TypeParamPool.Take(count).ToImmutableArray()
            select new MissingTypeArgsScenario(
                MissingArgsDeclKind.AbstractFromAbstract, genericName, derivedName, typeParams);

        return Gen.OneOf(genClassFromClass, genClassFromAbstract, genStructFromAbstract, genAbstractFromAbstract);
    }

    public static Arbitrary<MissingTypeArgsScenario> ArbitraryMissingTypeArgsScenario()
    {
        return GenMissingTypeArgsScenario().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 8: For any non-generic type that inherits from a generic type without
    /// providing type arguments, the compiler emits a MIST_MISSING_TYPE_ARGUMENTS
    /// diagnostic error.
    ///
    /// **Validates: Requirements 8.4, 13.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MissingTypeArgumentsPropertyTests) }, MaxTest = 100)]
    public bool MissingTypeArgs_EmitsDiagnostic(MissingTypeArgsScenario scenario)
    {
        TypeDeclarationNode genericTypeDecl;
        TypeDeclarationNode derivedTypeDecl;

        switch(scenario.Kind)
        {
            case MissingArgsDeclKind.ClassFromClass:
                {
                    // Generic base class
                    var genericClassDecl = new ClassDeclarationNode
                    {
                        Name = MakeToken(scenario.GenericTypeName),
                        TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                    };
                    genericTypeDecl = new ClassTypeTypeDeclarationNode { ClassDeclaration = genericClassDecl };

                    // Non-generic derived class inheriting without type args
                    var baseList = MakeBaseList([MakeBaseTypeRef(scenario.GenericTypeName)]);
                    var derivedClassDecl = new ClassDeclarationNode
                    {
                        Name = MakeToken(scenario.DerivedTypeName),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                        BaseClause = baseList,
                        BaseList = baseList,
                    };
                    derivedTypeDecl = new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl };
                    break;
                }
            case MissingArgsDeclKind.ClassFromAbstract:
                {
                    // Generic abstract type
                    var genericAbstractDecl = new AbstractDeclarationNode
                    {
                        Name = MakeToken(scenario.GenericTypeName),
                        TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
                        Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
                    };
                    genericTypeDecl = new AbstractTypeTypeDeclarationNode { AbstractDeclaration = genericAbstractDecl };

                    // Non-generic class implementing generic abstract type without type args
                    var baseList = MakeBaseList([MakeBaseTypeRef(scenario.GenericTypeName)]);
                    var derivedClassDecl = new ClassDeclarationNode
                    {
                        Name = MakeToken(scenario.DerivedTypeName),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                        BaseClause = baseList,
                        BaseList = baseList,
                    };
                    derivedTypeDecl = new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl };
                    break;
                }
            case MissingArgsDeclKind.StructFromAbstract:
                {
                    // Generic abstract type
                    var genericAbstractDecl = new AbstractDeclarationNode
                    {
                        Name = MakeToken(scenario.GenericTypeName),
                        TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
                        Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
                    };
                    genericTypeDecl = new AbstractTypeTypeDeclarationNode { AbstractDeclaration = genericAbstractDecl };

                    // Non-generic struct implementing generic abstract type without type args
                    var structDecl = new StructDeclarationNode
                    {
                        Name = MakeToken(scenario.DerivedTypeName),
                        Members = ImmutableArray<MemberDeclarationNode>.Empty,
                        BaseList = MakeBaseList([MakeBaseTypeRef(scenario.GenericTypeName)]),
                    };
                    derivedTypeDecl = new StructTypeTypeDeclarationNode { StructDeclaration = structDecl };
                    break;
                }
            case MissingArgsDeclKind.AbstractFromAbstract:
                {
                    // Generic abstract type
                    var genericAbstractDecl = new AbstractDeclarationNode
                    {
                        Name = MakeToken(scenario.GenericTypeName),
                        TypeParams = MakeTypeParamList(scenario.TypeParamNames.ToArray()),
                        Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
                    };
                    genericTypeDecl = new AbstractTypeTypeDeclarationNode { AbstractDeclaration = genericAbstractDecl };

                    // Non-generic abstract type extending generic abstract type without type args
                    var derivedAbstractDecl = new AbstractDeclarationNode
                    {
                        Name = MakeToken(scenario.DerivedTypeName),
                        Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
                        BaseList = MakeBaseList([MakeBaseTypeRef(scenario.GenericTypeName)]),
                    };
                    derivedTypeDecl = new AbstractTypeTypeDeclarationNode { AbstractDeclaration = derivedAbstractDecl };
                    break;
                }
            default:
                throw new InvalidOperationException();
        }

        var ast = MakeCompilationUnit(genericTypeDecl, derivedTypeDecl);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Should emit at least one MIST_MISSING_TYPE_ARGUMENTS diagnostic
        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        return missingArgsDiags.Count >= 1;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void NonGenericClass_InheritsGenericClass_EmitsMissingTypeArgsDiagnostic()
    {
        // class Base<T> { }
        // class Derived : Base { }  ← missing type arguments
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Base"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var baseList = MakeBaseList([MakeBaseTypeRef("Base")]);
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Derived"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        _ = Assert.Single(missingArgsDiags);
        Assert.Contains("Base", missingArgsDiags[0].Message);
    }

    [Fact]
    public void NonGenericClass_ImplementsGenericAbstract_EmitsMissingTypeArgsDiagnostic()
    {
        // abstract IContainer<T> { }
        // class Box : IContainer { }  ← missing type arguments
        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken("IContainer"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };

        var baseList = MakeBaseList([MakeBaseTypeRef("IContainer")]);
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Box"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        _ = Assert.Single(missingArgsDiags);
        Assert.Contains("IContainer", missingArgsDiags[0].Message);
    }

    [Fact]
    public void NonGenericStruct_ImplementsGenericAbstract_EmitsMissingTypeArgsDiagnostic()
    {
        // abstract IContainer<T> { }
        // struct Box : IContainer { }  ← missing type arguments
        var abstractDecl = new AbstractDeclarationNode
        {
            Name = MakeToken("IContainer"),
            TypeParams = MakeTypeParamList(["T"]),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };

        var structDecl = new StructDeclarationNode
        {
            Name = MakeToken("Box"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseList = MakeBaseList([MakeBaseTypeRef("IContainer")]),
        };

        var ast = MakeCompilationUnit(
            new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl },
            new StructTypeTypeDeclarationNode { StructDeclaration = structDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        _ = Assert.Single(missingArgsDiags);
        Assert.Contains("IContainer", missingArgsDiags[0].Message);
    }

    [Fact]
    public void MultipleTypeParams_MissingArgs_EmitsDiagnostic()
    {
        // class Map<K, V> { }
        // class MyMap : Map { }  ← missing type arguments
        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Map"),
            TypeParams = MakeTypeParamList(["K", "V"]),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var baseList = MakeBaseList([MakeBaseTypeRef("Map")]);
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("MyMap"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        _ = Assert.Single(missingArgsDiags);
        Assert.Contains("Map", missingArgsDiags[0].Message);
    }

    // ─── Task 6.8: Verify MIST_MISSING_TYPE_ARGUMENTS fires for dependent types ─

    [Fact]
    public void DependentTypeParam_MissingArgs_EmitsMissingTypeArgsDiagnostic()
    {
        // class FixedBuffer<Size:int> { }
        // class Program : FixedBuffer { }  ← missing dependent type arguments
        var depParam = new DependentTypeParameterTypeParameterNode
        {
            Name = MakeToken("Size"),
            ValueType = new IntTypePrimitiveTypeNode(),
        };

        var typeParamList = new TypeParameterListNode
        {
            TypeParameter = depParam,
            TypeParameters = ImmutableArray<TypeParameterNode>.Empty,
        };

        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("FixedBuffer"),
            TypeParams = typeParamList,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var baseList = MakeBaseList([MakeBaseTypeRef("FixedBuffer")]);
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        _ = Assert.Single(missingArgsDiags);
        Assert.Contains("FixedBuffer", missingArgsDiags[0].Message);
    }

    [Fact]
    public void MixedTypeParams_DependentAndRegular_MissingArgs_EmitsDiagnostic()
    {
        // class TypedBuffer<T, Size:int> { }
        // class Program : TypedBuffer { }  ← missing type arguments (both regular and dependent)
        var regularParam = new RegularTypeParameterTypeParameterNode { Name = MakeToken("T") };
        var depParam = new DependentTypeParameterTypeParameterNode
        {
            Name = MakeToken("Size"),
            ValueType = new IntTypePrimitiveTypeNode(),
        };

        var typeParamList = new TypeParameterListNode
        {
            TypeParameter = regularParam,
            TypeParameters = ImmutableArray.Create<TypeParameterNode>(depParam),
        };

        var baseClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("TypedBuffer"),
            TypeParams = typeParamList,
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        var baseList = MakeBaseList([MakeBaseTypeRef("TypedBuffer")]);
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
            BaseClause = baseList,
            BaseList = baseList,
        };

        var ast = MakeCompilationUnit(
            new ClassTypeTypeDeclarationNode { ClassDeclaration = baseClassDecl },
            new ClassTypeTypeDeclarationNode { ClassDeclaration = derivedClassDecl });

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var missingArgsDiags = diagnostics
            .Where(d => d.Id == GenericsDiagnostics.MissingTypeArguments.Id)
            .ToList();

        _ = Assert.Single(missingArgsDiags);
        Assert.Contains("TypedBuffer", missingArgsDiags[0].Message);
    }
}