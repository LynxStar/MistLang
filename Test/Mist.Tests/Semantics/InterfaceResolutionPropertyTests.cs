// Feature: interfaces, Property 3: Interface resolution on implementing types
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
/// Property-based tests verifying interface resolution on implementing types.
///
/// Property 3: For any valid program where classes, structs, or interfaces declare
/// interface lists referencing defined interfaces (including forward references),
/// the semantic model builder SHALL resolve all interface names to InterfaceType
/// instances and store them on the implementing type's Interfaces property. The count
/// of resolved interfaces SHALL equal the count of interface names in the declaration.
/// </summary>
public class InterfaceResolutionPropertyTests
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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
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

    private static ClassDeclarationNode MakeClassDecl(
        string name,
        string? interfaceName = null,
        ImmutableArray<MemberDeclarationNode>? members = null)
    {
        var decl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members ?? ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // For a class implementing an interface (no base class), the interface
        // name goes in the BaseClause/BaseClassName fields. The semantic model
        // builder checks whether the first name resolves to a ClassType or
        // InterfaceType to determine if it's a base class or interface.
        if (interfaceName is not null)
        {
            var _baseList1 = MakeBaseList(interfaceName);

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

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Types = typeDecls.ToImmutableArray(),
        };
    }

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode decl) =>
        new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = decl };

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode decl) =>
        new ClassTypeTypeDeclarationNode { ClassDeclaration = decl };

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode decl) =>
        new StructTypeTypeDeclarationNode { StructDeclaration = decl };

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario where a type implements a single interface.
    /// The Storm generator only captures the first interface name after the colon,
    /// so we test single-interface resolution.
    /// </summary>
#pragma warning disable CA1034
    public enum ImplementingTypeKind
    {
        Class,
        Struct,
        Interface,
    }

    /// <summary>
    /// A test scenario: an interface is declared, and an implementing type
    /// references it. The interface is declared before or after the implementing
    /// type (forward reference).
    /// </summary>
    public record ResolutionScenario(
        string InterfaceName,
        ImmutableArray<string> InterfaceMethodNames,
        string ImplementingTypeName,
        ImplementingTypeKind TypeKind,
        bool ForwardReference)
    {
        public override string ToString()
        {
            var methods = InterfaceMethodNames.Length > 0
                ? $"{{ {string.Join(", ", InterfaceMethodNames)} }}"
                : "{ }";
            var direction = ForwardReference ? "forward-ref" : "declared-first";
            return $"{TypeKind} {ImplementingTypeName} : {InterfaceName} {methods} ({direction})";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta"];

    private static readonly string[] TypeNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];

    public static Gen<ResolutionScenario> GenResolutionScenario()
    {
        return from interfaceIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from typeIdx in Gen.Choose(0, TypeNamePool.Length - 1)
               from typeKind in Gen.Elements(
                   ImplementingTypeKind.Class,
                   ImplementingTypeKind.Struct,
                   ImplementingTypeKind.Interface)
               from methodCount in Gen.Choose(0, 3)
               from methods in Gen.Shuffle(MethodNamePool)
                   .Select(m => m.Take(methodCount).ToImmutableArray())
               from forwardRef in Gen.Elements(true, false)
               let interfaceName = InterfaceNamePool[interfaceIdx]
               let typeName = typeKind == ImplementingTypeKind.Interface
                   ? "I" + TypeNamePool[typeIdx]
                   : TypeNamePool[typeIdx]
               select new ResolutionScenario(
                   interfaceName,
                   methods,
                   typeName,
                   typeKind,
                   forwardRef);
    }

    public static Arbitrary<ResolutionScenario> ArbitraryResolutionScenario() =>
        GenResolutionScenario().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any valid program where classes, structs, or interfaces
    /// declare interface lists referencing defined interfaces (including forward
    /// references), the semantic model builder resolves all interface names to
    /// InterfaceType instances and stores them on the implementing type's Interfaces
    /// property. The count of resolved interfaces equals the count of interface
    /// names in the declaration.
    ///
    /// **Validates: Requirements 5.1, 5.4, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceResolutionPropertyTests) }, MaxTest = 100)]
    public bool InterfaceResolution_ResolvesInterfaceOnImplementingType(ResolutionScenario scenario)
    {
        // Ensure the interface name and implementing type name are different
        if (scenario.InterfaceName == scenario.ImplementingTypeName)
            return true; // skip degenerate case

        // Build the interface declaration
        var interfaceMethods = scenario.InterfaceMethodNames
            .Select(m => MakeInterfaceMethod(m))
            .ToImmutableArray();
        var interfaceDecl = MakeInterfaceDecl(scenario.InterfaceName, interfaceMethods);

        // Build the implementing type declaration
        TypeDeclarationNode implementingTypeDecl;
        switch (scenario.TypeKind)
        {
            case ImplementingTypeKind.Class:
                implementingTypeDecl = WrapClass(
                    MakeClassDecl(scenario.ImplementingTypeName, scenario.InterfaceName));
                break;
            case ImplementingTypeKind.Struct:
                implementingTypeDecl = WrapStruct(
                    MakeStructDecl(scenario.ImplementingTypeName, scenario.InterfaceName));
                break;
            case ImplementingTypeKind.Interface:
                implementingTypeDecl = WrapInterface(
                    MakeInterfaceDecl(scenario.ImplementingTypeName,
                        ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
                        scenario.InterfaceName));
                break;
            default:
                return false;
        }

        // Arrange type declarations: forward reference means implementing type
        // is declared before the interface
        TypeDeclarationNode[] typeDecls;
        if (scenario.ForwardReference)
        {
            typeDecls = [implementingTypeDecl, WrapInterface(interfaceDecl)];
        }
        else
        {
            typeDecls = [WrapInterface(interfaceDecl), implementingTypeDecl];
        }

        var ast = MakeCompilationUnit(typeDecls);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Find the implementing type's symbol
        var expectedKind = scenario.TypeKind switch
        {
            ImplementingTypeKind.Class => SymbolKind.Class,
            ImplementingTypeKind.Struct => SymbolKind.Struct,
            ImplementingTypeKind.Interface => SymbolKind.Interface,
            _ => SymbolKind.Class,
        };

        var implementingSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.ImplementingTypeName && s.Kind == expectedKind);
        if (implementingSymbol is null)
            return false;

        // Get the Interfaces property from the implementing type
        IReadOnlyList<IType> resolvedInterfaces;
        switch (implementingSymbol.DeclaredType)
        {
            case ClassType classType:
                resolvedInterfaces = classType.Interfaces;
                break;
            case StructType structType:
                resolvedInterfaces = structType.Interfaces;
                break;
            case InterfaceType interfaceType:
                resolvedInterfaces = interfaceType.Interfaces;
                break;
            default:
                return false;
        }

        // Requirement 5.1: The interface name is resolved to an InterfaceType
        // The count of resolved interfaces SHALL equal the count of interface
        // names in the declaration (1 in our case, since Storm only captures
        // the first interface name).
        if (resolvedInterfaces.Count != 1)
            return false;

        // Requirement 5.5: The resolved interface is the correct InterfaceType
        var resolved = resolvedInterfaces[0];
        if (resolved.DisplayName != scenario.InterfaceName)
            return false;

        // Verify the resolved InterfaceType has the correct kind
        if (resolved.Kind != TypeKind.Interface)
            return false;

        // Requirement 5.4: For classes, the base class and interfaces are
        // stored separately. Since we're implementing an interface (not
        // inheriting a class), BaseType should be null.
        if (implementingSymbol.DeclaredType is ClassType ct && ct.BaseType is not null)
            return false;

        // No error diagnostics should be emitted for valid resolution
        var errorDiagnostics = diagnostics.Where(d =>
            d.Id.Value == "MIST_UNDEFINED_INTERFACE" ||
            d.Id.Value == "MIST_NOT_AN_INTERFACE").ToList();
        if (errorDiagnostics.Count > 0)
            return false;

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void ClassImplementingInterface_ResolvesInterfaceOnClassType()
    {
        var interfaceDecl = MakeInterfaceDecl("IWorker",
            ImmutableArray.Create(MakeInterfaceMethod("DoWork")));
        var classDecl = MakeClassDecl("Worker", "IWorker");

        var ast = MakeCompilationUnit(
            WrapInterface(interfaceDecl),
            WrapClass(classDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "Worker" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);

        Assert.Single(classType.Interfaces);
        Assert.Equal("IWorker", classType.Interfaces[0].DisplayName);
        Assert.Null(classType.BaseType);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_INTERFACE");
    }

    [Fact]
    public void StructImplementingInterface_ResolvesInterfaceOnStructType()
    {
        var interfaceDecl = MakeInterfaceDecl("IComparable",
            ImmutableArray.Create(MakeInterfaceMethod("CompareTo")));
        var structDecl = MakeStructDecl("Point", "IComparable");

        var ast = MakeCompilationUnit(
            WrapInterface(interfaceDecl),
            WrapStruct(structDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var structSymbol = model.AllSymbols.First(s => s.Name == "Point" && s.Kind == SymbolKind.Struct);
        var structType = Assert.IsType<StructType>(structSymbol.DeclaredType);

        Assert.Single(structType.Interfaces);
        Assert.Equal("IComparable", structType.Interfaces[0].DisplayName);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_INTERFACE");
    }

    [Fact]
    public void InterfaceInheritingInterface_ResolvesInterfaceOnInterfaceType()
    {
        var baseInterface = MakeInterfaceDecl("IBase",
            ImmutableArray.Create(MakeInterfaceMethod("BaseMethod")));
        var derivedInterface = MakeInterfaceDecl("IDerived",
            ImmutableArray<InterfaceMethodDeclarationNode>.Empty,
            "IBase");

        var ast = MakeCompilationUnit(
            WrapInterface(baseInterface),
            WrapInterface(derivedInterface));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var derivedSymbol = model.AllSymbols.First(s => s.Name == "IDerived" && s.Kind == SymbolKind.Interface);
        var derivedType = Assert.IsType<InterfaceType>(derivedSymbol.DeclaredType);

        Assert.Single(derivedType.Interfaces);
        Assert.Equal("IBase", derivedType.Interfaces[0].DisplayName);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_INTERFACE");
    }

    [Fact]
    public void ForwardReference_ClassImplementsInterfaceDeclaredAfter()
    {
        // The class is declared before the interface — forward reference
        var classDecl = MakeClassDecl("Worker", "IWorker");
        var interfaceDecl = MakeInterfaceDecl("IWorker",
            ImmutableArray.Create(MakeInterfaceMethod("DoWork")));

        var ast = MakeCompilationUnit(
            WrapClass(classDecl),
            WrapInterface(interfaceDecl));
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        var classSymbol = model.AllSymbols.First(s => s.Name == "Worker" && s.Kind == SymbolKind.Class);
        var classType = Assert.IsType<ClassType>(classSymbol.DeclaredType);

        Assert.Single(classType.Interfaces);
        Assert.Equal("IWorker", classType.Interfaces[0].DisplayName);
        Assert.Null(classType.BaseType);
    }

    [Fact]
    public void ClassWithBaseClassAndNoInterface_BaseTypeIsSetInterfacesEmpty()
    {
        // When a class has a base class (not an interface), BaseType is set
        // and Interfaces remains empty.
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
        Assert.Empty(dogType.Interfaces);
    ;
    }

}
