// Feature: abstracts, Property 3: Abstract resolution on implementing types
// **Validates: Requirements 5.1, 5.4, 5.5**

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
/// Property-based tests verifying abstract resolution on implementing types.
///
/// Property 3: For any valid program where classes, structs, or abstracts declare
/// abstract lists referencing defined abstracts (including forward references),
/// the semantic model builder SHALL resolve all abstract names to AbstractType
/// instances and store them on the implementing type's Abstracts property. The count
/// of resolved abstracts SHALL equal the count of abstract names in the declaration.
/// </summary>
public class AbstractResolutionPropertyTests
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

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? abstractName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // For a class implementing an abstract (no base class), the abstract
        // name goes in the BaseClause/BaseClassName fields. The semantic model
        // builder checks whether the first name resolves to a ClassType or
        // AbstractType to determine if it's a base class or abstract.
        if (abstractName is not null)
        {
            var _baseList1 = MakeBaseList(abstractName);

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

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = (typeDecls.ToImmutableArray()).AsTopLevelItems(),
        };
    }

    private static TypeDeclarationNode WrapAbstract(AbstractDeclarationNode decl) =>
        new AbstractTypeTypeDeclarationNode { AbstractDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario where a type implements a single abstract.
    /// The Storm generator only captures the first abstract name after the colon,
    /// so we test single-abstract resolution.
    /// </summary>
#pragma warning disable CA1034
    public enum ImplementingTypeKind
    {
        Class,
        Struct,
        Abstract,
    }

    /// <summary>
    /// A test scenario: an abstract is declared, and an implementing type
    /// references it. The abstract is declared before or after the implementing
    /// type (forward reference).
    /// </summary>
    public record ResolutionScenario(
        string AbstractName,
        ImmutableArray<string> AbstractMethodNames,
        string ImplementingTypeName,
        ImplementingTypeKind TypeKind,
        bool ForwardReference)
    {
        public override string ToString()
        {
            var methods = AbstractMethodNames.Length > 0
                ? $"{{ {string.Join(", ", AbstractMethodNames)} }}"
                : "{ }";
            var direction = ForwardReference ? "forward-ref" : "declared-first";
            return $"{TypeKind} {ImplementingTypeName} : {AbstractName} {methods} ({direction})";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] TypeNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];

    public static Gen<ResolutionScenario> GenResolutionScenario()
    {
        return from abstractIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(
                   ImplementingTypeKind.Class,
                   ImplementingTypeKind.Struct,
                   ImplementingTypeKind.Abstract)
               from methodCount in Gen.Choose(0, 3)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               from forwardRef in Gen.Elements(true, false)
               let abstractName = AbstractNamePool[abstractIdx]
               let typeName = typeKind == ImplementingTypeKind.Abstract
                   ? "I" + TypeNamePool[typeIdx]
                   : TypeNamePool[typeIdx]
               select new ResolutionScenario(
                   abstractName,
                   methods,
                   typeName,
                   typeKind,
                   forwardRef);
    }

    public static Arbitrary<ResolutionScenario> ArbitraryResolutionScenario() =>
        GenResolutionScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any valid program where classes, structs, or abstracts
    /// declare abstract lists referencing defined abstracts (including forward
    /// references), the semantic model builder resolves all abstract names to
    /// AbstractType instances and stores them on the implementing type's Abstracts
    /// property. The count of resolved abstracts equals the count of abstract
    /// names in the declaration.
    ///
    /// **Validates: Requirements 5.1, 5.4, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractResolutionPropertyTests) }, MaxTest = 100)]
    public bool AbstractResolution_ResolvesAbstractOnImplementingType(ResolutionScenario scenario)
    {
        // Ensure the abstract name and implementing type name are different
        if (scenario.AbstractName == scenario.ImplementingTypeName)
            return true; // skip degenerate case

        // Build the abstract declaration
        var abstractMethods = scenario.AbstractMethodNames
            .Select(m => MakeAbstractMethod(m))
            .ToImmutableArray();
        var abstractDecl = MakeAbstractDecl(scenario.AbstractName, abstractMethods);

        // Build the implementing type declaration
        TypeDeclarationNode implementingTypeDecl;
        switch (scenario.TypeKind)
        {
            case ImplementingTypeKind.Class:
                implementingTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.AbstractName));
                break;
            case ImplementingTypeKind.Struct:
                implementingTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.AbstractName));
                break;
            case ImplementingTypeKind.Abstract:
                implementingTypeDecl = WrapAbstract(
                    MakeAbstractDecl(scenario.ImplementingTypeName,
                        ImmutableArray<AbstractMethodDeclarationNode>.Empty,
                        scenario.AbstractName));
                break;
            default:
                return false;
        }

        // Arrange type declarations: forward reference means implementing type
        // is declared before the abstract
        TypeDeclarationNode[] typeDecls;
        if (scenario.ForwardReference)
        {
            typeDecls = [implementingTypeDecl, WrapAbstract(abstractDecl)];
        }
        else
        {
            typeDecls = [WrapAbstract(abstractDecl), implementingTypeDecl];
        }

        var ast = MakeCompilationUnit(typeDecls);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Find the implementing type's symbol
        var expectedKind = scenario.TypeKind switch
        {
            ImplementingTypeKind.Class => SymbolKind.Class,
            ImplementingTypeKind.Struct => SymbolKind.Struct,
            ImplementingTypeKind.Abstract => SymbolKind.Abstract,
            _ => SymbolKind.Class,
        };

        var implementingSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ImplementingTypeName && s.Kind == expectedKind);
        if (implementingSymbol is null)
            return false;

        // Get the Abstracts property from the implementing type
        IReadOnlyList<IType> resolvedAbstracts;
        switch (implementingSymbol.DeclaredType)
        {
            case ClassType classType:
                resolvedAbstracts = classType.Abstracts;
                break;
            case StructType structType:
                resolvedAbstracts = structType.Abstracts;
                break;
            case AbstractType abstractType:
                resolvedAbstracts = abstractType.Abstracts;
                break;
            default:
                return false;
        }

        // Requirement 5.1: The abstract name is resolved to an AbstractType
        // The count of resolved abstracts SHALL equal the count of abstract
        // names in the declaration (1 in our case, since Storm only captures
        // the first abstract name).
        if (resolvedAbstracts.Count != 1)
            return false;

        // Requirement 5.5: The resolved abstract is the correct AbstractType
        var resolved = resolvedAbstracts[0];
        if (resolved.DisplayName != scenario.AbstractName)
            return false;

        // Verify the resolved AbstractType has the correct kind
        if (resolved.Kind != TypeKind.Abstract)
            return false;

        // Requirement 5.4: For classes, the base class and abstracts are
        // stored separately. Since we're implementing an abstract (not
        // inheriting a class), BaseType should be null.
        if (implementingSymbol.DeclaredType is ClassType ct && ct.BaseType is not null)
            return false;

        // No error diagnostics should be emitted for valid resolution
        var errorDiagnostics = diagnostics.Where(d =>
            d.Id.Value == "MIST_UNDEFINED_ABSTRACT" ||
            d.Id.Value == "MIST_NOT_AN_ABSTRACT").ToList();
        if (errorDiagnostics.Count > 0)
            return false;

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassImplementingAbstract_ResolvesAbstractOnClassType()
    {
        var abstractDecl = MakeAbstractDecl("IWorker",
            ImmutableArray.Create(MakeAbstractMethod("DoWork")));
        var classDecl = MakeClassDecl("Worker", "IWorker");

        var ast = MakeCompilationUnit(
            WrapAbstract(abstractDecl),
            WrapClass(classDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "Worker" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);

        Assert.Single(classType.Abstracts);
        Assert.Equal("IWorker", classType.Abstracts[0].DisplayName);
        Assert.Null(classType.BaseType);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_ABSTRACT");
    }

    [Fact]
    public void StructImplementingAbstract_ResolvesAbstractOnStructType()
    {
        var abstractDecl = MakeAbstractDecl("IComparable",
            ImmutableArray.Create(MakeAbstractMethod("CompareTo")));
        var structDecl = MakeStructDecl("Point", "IComparable");

        var ast = MakeCompilationUnit(
            WrapAbstract(abstractDecl),
            WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var structSymbol = model.AllSymbols.First(s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);

        Assert.Single(structType.Abstracts);
        Assert.Equal("IComparable", structType.Abstracts[0].DisplayName);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_ABSTRACT");
    }

    [Fact]
    public void AbstractInheritingAbstract_ResolvesAbstractOnAbstractType()
    {
        var baseAbstract = MakeAbstractDecl("IBase",
            ImmutableArray.Create(MakeAbstractMethod("BaseMethod")));
        var derivedAbstract = MakeAbstractDecl("IDerived",
            ImmutableArray<AbstractMethodDeclarationNode>.Empty,
            "IBase");

        var ast = MakeCompilationUnit(
            WrapAbstract(baseAbstract),
            WrapAbstract(derivedAbstract));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var derivedSymbol = model.AllSymbols.First(s => s.Name == "IDerived" && s.Kind == SymbolKind.Abstract);
        var derivedType = Assert.IsType<AbstractType>(derivedSymbol.DeclaredType);

        Assert.Single(derivedType.Abstracts);
        Assert.Equal("IBase", derivedType.Abstracts[0].DisplayName);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_ABSTRACT");
    }

    [Fact]
    public void ForwardReference_ClassImplementsAbstractDeclaredAfter()
    {
        // The class is declared before the abstract — forward reference
        var classDecl = MakeClassDecl("Worker", "IWorker");
        var abstractDecl = MakeAbstractDecl("IWorker",
            ImmutableArray.Create(MakeAbstractMethod("DoWork")));

        var ast = MakeCompilationUnit(
            WrapClass(classDecl),
            WrapAbstract(abstractDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "Worker" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);

        Assert.Single(classType.Abstracts);
        Assert.Equal("IWorker", classType.Abstracts[0].DisplayName);
        Assert.Null(classType.BaseType);
    }

    [Fact]
    public void ClassWithBaseClassAndNoAbstract_BaseTypeIsSetAbstractsEmpty()
    {
        // When a class has a base class (not an abstract), BaseType is set
        // and Abstracts remains empty.
        var baseClassDecl = MakeClassDecl("Animal");
        var derivedClassDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Dog"),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var _baseList2 = MakeBaseList("Animal");

        derivedClassDecl.BaseClause = _baseList2;

        derivedClassDecl.BaseList = _baseList2;

        var ast = MakeCompilationUnit(
            WrapClass(baseClassDecl),
            WrapClass(derivedClassDecl));
        var (model, _) = BuildModelWithDiagnostics(ast);

        var dogSymbol = model.AllSymbols.First(s => s.Name == "Dog" && s.Kind == SymbolKind.Class);
        var dogType = Assert.IsType<ClassType>(dogSymbol.DeclaredType);

        Assert.NotNull(dogType.BaseType);
        Assert.Equal("Animal", dogType.BaseType!.DisplayName);
        Assert.Empty(dogType.Abstracts);
    ;
    }

}
