// Feature: interfaces, Property 2: Interface type registration
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

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
/// Property-based tests verifying interface type registration in the semantic model.
///
/// Property 2: For any set of interface declarations with varying numbers of methods,
/// the semantic model builder SHALL create an InterfaceType for each interface with
/// TypeKind.Interface, register a symbol with SymbolKind.Interface in the parent scope,
/// and register a SymbolKind.Method symbol for each interface method in the interface's
/// member scope. The count of method symbols in the interface's member scope SHALL equal
/// the number of methods declared in the interface.
/// </summary>
public class InterfaceTypeRegistrationPropertyTests
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
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates an InterfaceMethodDeclarationNode with the given name and return type.
    /// </summary>
    private static InterfaceMethodDeclarationNode MakeInterfaceMethod(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new InterfaceMethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new VoidTypeRefTypeReferenceNode(),
            Parameters = parameters,
        };
    }

    /// <summary>
    /// Creates an InterfaceDeclarationNode with the given name and methods.
    /// </summary>
    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        ImmutableArray<InterfaceMethodDeclarationNode> methods)
    {
        return new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Methods = methods,
        };
    }

    /// <summary>
    /// Wraps interface declarations into a CompilationUnitNode.
    /// </summary>
    private static CompilationUnitNode MakeCompilationUnit(
        params InterfaceDeclarationNode[] interfaceDecls)
    {
        var types = interfaceDecls.Select(i =>
            (TypeDeclarationNode)new InterfaceTypeTypeDeclarationNode
            {
                InterfaceDeclaration = i,
            }).ToImmutableArray();

        return new CompilationUnitNode { Types = types };
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a set of interface declarations for testing type registration.
    /// Each entry is (interfaceName, list of method names).
    /// </summary>
#pragma warning disable CA1034
    public record InterfaceSet(ImmutableArray<(string Name, ImmutableArray<string> MethodNames)> Interfaces)
    {
        public override string ToString()
        {
            var entries = Interfaces.Select(i =>
            {
                var methods = i.MethodNames.Length > 0
                    ? $"{{ {string.Join(", ", i.MethodNames)} }}"
                    : "{ }";
                return $"{i.Name} {methods}";
            });
            return $"[{string.Join("; ", entries)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Pool of unique interface names to draw from.
    /// </summary>
    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta", "IEta", "ITheta"];

    /// <summary>
    /// Pool of unique method names to draw from.
    /// </summary>
    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    /// <summary>
    /// Generates an InterfaceSet with 1-5 interfaces, each having 0-4 methods.
    /// Method names within each interface are unique.
    /// </summary>
    public static Gen<InterfaceSet> GenInterfaceSet()
    {
        return from interfaceCount in Gen.Choose(1, 5)
               from names in Gen.Shuffle(InterfaceNamePool)
                   .Select(n => n.Take(interfaceCount).ToArray())
               from interfaces in GenInterfaceEntries(names)
               select new InterfaceSet(interfaces.ToImmutableArray());
    }

    private static Gen<List<(string Name, ImmutableArray<string> MethodNames)>> GenInterfaceEntries(
        string[] interfaceNames)
    {
        var gen = Gen.Constant(new List<(string Name, ImmutableArray<string> MethodNames)>());

        foreach (var name in interfaceNames)
        {
            gen = from entries in gen
                  from methodCount in Gen.Choose(0, 4)
                  from methods in Gen.Shuffle(MethodNamePool)
                      .Select(m => m.Take(methodCount).ToImmutableArray())
                  select AppendEntry(entries, name, methods);
        }

        return gen;
    }

    private static List<(string Name, ImmutableArray<string> MethodNames)> AppendEntry(
        List<(string Name, ImmutableArray<string> MethodNames)> entries,
        string name,
        ImmutableArray<string> methodNames)
    {
        var result = new List<(string Name, ImmutableArray<string> MethodNames)>(entries)
        {
            (name, methodNames),
        };
        return result;
    }

    /// <summary>FsCheck Arbitrary registration for InterfaceSet.</summary>
    public static Arbitrary<InterfaceSet> ArbitraryInterfaceSet() =>
        GenInterfaceSet().ToArbitrary();

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any set of interface declarations with varying numbers of methods,
    /// the semantic model builder creates an InterfaceType for each interface with
    /// TypeKind.Interface, registers a symbol with SymbolKind.Interface in the parent scope,
    /// and registers a SymbolKind.Method symbol for each interface method in the interface's
    /// member scope. The count of method symbols equals the number of declared methods.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceTypeRegistrationPropertyTests) }, MaxTest = 100)]
    public bool InterfaceTypeRegistration_AllInterfacesCorrectlyRegistered(InterfaceSet interfaceSet)
    {
        // Build AST from the interface set
        var interfaceDecls = interfaceSet.Interfaces.Select(i =>
        {
            var methods = i.MethodNames.Select(m => MakeInterfaceMethod(m)).ToImmutableArray();
            return MakeInterfaceDecl(i.Name, methods);
        }).ToArray();

        var ast = MakeCompilationUnit(interfaceDecls);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        foreach (var (name, methodNames) in interfaceSet.Interfaces)
        {
            // Requirement 4.2: A symbol with SymbolKind.Interface is registered in the parent scope
            var interfaceSymbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Interface);
            if (interfaceSymbol is null)
                return false;

            // Requirement 4.1, 4.4: An InterfaceType is created with TypeKind.Interface
            if (interfaceSymbol.DeclaredType is not InterfaceType interfaceType)
                return false;
            if (interfaceType.Kind != TypeKind.Interface)
                return false;
            if (interfaceType.DisplayName != name)
                return false;

            // Requirement 4.3: Each interface method is registered as SymbolKind.Method
            // in the interface's member scope
            var memberScope = interfaceType.MemberScope;
            var methodSymbols = memberScope.Members
                .Where(m => m.Kind == SymbolKind.Method)
                .ToList();

            // The count of method symbols SHALL equal the number of methods declared
            if (methodSymbols.Count != methodNames.Length)
                return false;

            // Each declared method name should have a corresponding method symbol
            foreach (var methodName in methodNames)
            {
                if (!methodSymbols.Any(m => m.Name == methodName))
                    return false;
            }
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void EmptyInterface_RegistersInterfaceTypeWithNoMethods()
    {
        var interfaceDecl = MakeInterfaceDecl("IEmpty", ImmutableArray<InterfaceMethodDeclarationNode>.Empty);
        var ast = MakeCompilationUnit(interfaceDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "IEmpty" && s.Kind == SymbolKind.Interface);
        Assert.NotNull(symbol);

        var interfaceType = Assert.IsType<InterfaceType>(symbol.DeclaredType);
        Assert.Equal(TypeKind.Interface, interfaceType.Kind);
        Assert.Equal("IEmpty", interfaceType.DisplayName);
        Assert.Empty(interfaceType.MemberScope.Members);
    }

    [Fact]
    public void InterfaceWithMethods_RegistersMethodSymbolsInMemberScope()
    {
        var methods = ImmutableArray.Create(
            MakeInterfaceMethod("DoWork"),
            MakeInterfaceMethod("Process"),
            MakeInterfaceMethod("Execute"));
        var interfaceDecl = MakeInterfaceDecl("IWorker", methods);
        var ast = MakeCompilationUnit(interfaceDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "IWorker" && s.Kind == SymbolKind.Interface);
        Assert.NotNull(symbol);

        var interfaceType = Assert.IsType<InterfaceType>(symbol.DeclaredType);
        var memberMethods = interfaceType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Method)
            .ToList();

        Assert.Equal(3, memberMethods.Count);
        Assert.Contains(memberMethods, m => m.Name == "DoWork");
        Assert.Contains(memberMethods, m => m.Name == "Process");
        Assert.Contains(memberMethods, m => m.Name == "Execute");
    }

    [Fact]
    public void MultipleInterfaces_AllRegisteredCorrectly()
    {
        var interface1 = MakeInterfaceDecl("IFoo", ImmutableArray.Create(MakeInterfaceMethod("Foo")));
        var interface2 = MakeInterfaceDecl("IBar", ImmutableArray<InterfaceMethodDeclarationNode>.Empty);
        var interface3 = MakeInterfaceDecl("IBaz", ImmutableArray.Create(
            MakeInterfaceMethod("Baz1"),
            MakeInterfaceMethod("Baz2")));

        var ast = MakeCompilationUnit(interface1, interface2, interface3);
        var (model, _) = BuildModelWithDiagnostics(ast);

        // IFoo: 1 method
        var fooSymbol = model.AllSymbols.First(s => s.Name == "IFoo" && s.Kind == SymbolKind.Interface);
        var fooType = Assert.IsType<InterfaceType>(fooSymbol.DeclaredType);
        Assert.Single(fooType.MemberScope.Members, m => m.Kind == SymbolKind.Method);

        // IBar: 0 methods
        var barSymbol = model.AllSymbols.First(s => s.Name == "IBar" && s.Kind == SymbolKind.Interface);
        var barType = Assert.IsType<InterfaceType>(barSymbol.DeclaredType);
        Assert.DoesNotContain(barType.MemberScope.Members, m => m.Kind == SymbolKind.Method);

        // IBaz: 2 methods
        var bazSymbol = model.AllSymbols.First(s => s.Name == "IBaz" && s.Kind == SymbolKind.Interface);
        var bazType = Assert.IsType<InterfaceType>(bazSymbol.DeclaredType);
        Assert.Equal(2, bazType.MemberScope.Members.Count(m => m.Kind == SymbolKind.Method));
    }
}
