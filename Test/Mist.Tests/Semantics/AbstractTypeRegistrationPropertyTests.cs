// Feature: abstracts, Property 2: Abstract type registration
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying abstract type registration in the semantic model.
///
/// Property 2: For any set of abstract declarations with varying numbers of methods,
/// the semantic model builder SHALL create an AbstractType for each abstract with
/// TypeKind.Abstract, register a symbol with SymbolKind.Abstract in the parent scope,
/// and register a SymbolKind.Method symbol for each abstract method in the abstract's
/// member scope. The count of method symbols in the abstract's member scope SHALL equal
/// the number of methods declared in the abstract.
/// </summary>
public class AbstractTypeRegistrationPropertyTests
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

    /// <summary>
    /// Creates an MethodDeclarationNode with the given name and return type.
    /// </summary>
    private static MethodDeclarationNode MakeAbstractMethod(
        string name,
        TypeReferenceNode? returnType = null,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken(name),
            ReturnType = returnType ?? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = parameters,
            Body = new CallableNoBodyCallableBodyNode(),
        };
    }

    /// <summary>
    /// Creates an TypeBodyDeclarationNode with the given name and methods.
    /// </summary>
    private static TypeBodyDeclarationNode MakeAbstractDecl(
        string name,
        ImmutableArray<MethodDeclarationNode> methods)
    {
        var members = methods.Select(m =>
            (MemberDeclarationNode)new MethodMemberMemberDeclarationNode { Method = m })
            .ToImmutableArray();
        return new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
        };
    }

    /// <summary>
    /// Wraps abstract declarations into a CompilationUnitNode.
    /// </summary>
    private static CompilationUnitNode MakeCompilationUnit(
        params TypeBodyDeclarationNode[] abstractDecls)
    {
        var types = abstractDecls.Select(i =>
            (TypeDeclarationNode)new BodyTypeTypeDeclarationNode
            {
                TypeBody = i,
            }).ToImmutableArray();

        return new CompilationUnitNode { Items = types.AsTopLevelItems() };
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a set of abstract declarations for testing type registration.
    /// Each entry is (abstractName, list of method names).
    /// </summary>
#pragma warning disable CA1034
    public record AbstractSet(ImmutableArray<(string Name, ImmutableArray<string> MethodNames)> Abstracts)
    {
        public override string ToString()
        {
            var entries = Abstracts.Select(i =>
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
    /// Pool of unique abstract names to draw from.
    /// </summary>
    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta", "IEta", "ITheta"];

    /// <summary>
    /// Pool of unique method names to draw from.
    /// </summary>
    private static readonly string[] MethodNamePool =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform", "Validate", "Render"];

    /// <summary>
    /// Generates an AbstractSet with 1-5 abstracts, each having 0-4 methods.
    /// Method names within each abstract are unique.
    /// </summary>
    public static Gen<AbstractSet> GenAbstractSet()
    {
        return from abstractCount in Gen.Choose(1, 5)
               from names in Gen.Shuffle(AbstractNamePool)
                   .Select(n => n.Take(abstractCount).ToArray())
               from abstracts in GenAbstractEntries(names)
               select new AbstractSet(abstracts.ToImmutableArray());
    }

    private static Gen<List<(string Name, ImmutableArray<string> MethodNames)>> GenAbstractEntries(
        string[] abstractNames)
    {
        var gen = Gen.Constant(new List<(string Name, ImmutableArray<string> MethodNames)>());

        foreach(var name in abstractNames)
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

    /// <summary>FsCheck Arbitrary registration for AbstractSet.</summary>
    public static Arbitrary<AbstractSet> ArbitraryAbstractSet()
    {
        return GenAbstractSet().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any set of abstract declarations with varying numbers of methods,
    /// the semantic model builder creates an AbstractType for each abstract with
    /// TypeKind.Abstract, registers a symbol with SymbolKind.Abstract in the parent scope,
    /// and registers a SymbolKind.Method symbol for each abstract method in the abstract's
    /// member scope. The count of method symbols equals the number of declared methods.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractTypeRegistrationPropertyTests) }, MaxTest = 100)]
    public bool AbstractTypeRegistration_AllAbstractsCorrectlyRegistered(AbstractSet abstractSet)
    {
        // Build AST from the abstract set
        var abstractDecls = abstractSet.Abstracts.Select(i =>
        {
            var methods = i.MethodNames.Select(m => MakeAbstractMethod(m)).ToImmutableArray();
            return MakeAbstractDecl(i.Name, methods);
        }).ToArray();

        var ast = MakeCompilationUnit(abstractDecls);
        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        foreach(var (name, methodNames) in abstractSet.Abstracts)
        {
            // Requirement 4.2: A symbol with SymbolKind.Abstract is registered in the parent scope
            var abstractSymbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Abstract);
            if(abstractSymbol is null)
            {
                return false;
            }

            // Requirement 4.1, 4.4: An AbstractType is created with TypeKind.Abstract
            if(abstractSymbol.DeclaredType is not AbstractType abstractType)
            {
                return false;
            }

            if(abstractType.Kind != TypeKind.Abstract)
            {
                return false;
            }

            if(abstractType.DisplayName != name)
            {
                return false;
            }

            // Requirement 4.3: Each abstract method is registered as SymbolKind.Method
            // in the abstract's member scope
            var memberScope = abstractType.MemberScope;
            var methodSymbols = memberScope.Members
                .Where(m => m.Kind == SymbolKind.Method)
                .ToList();

            // The count of method symbols SHALL equal the number of methods declared
            if(methodSymbols.Count != methodNames.Length)
            {
                return false;
            }

            // Each declared method name should have a corresponding method symbol
            foreach(var methodName in methodNames)
            {
                if(!methodSymbols.Any(m => m.Name == methodName))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void EmptyAbstract_RegistersAbstractTypeWithNoMethods()
    {
        var abstractDecl = MakeAbstractDecl("IEmpty", ImmutableArray<MethodDeclarationNode>.Empty);
        var ast = MakeCompilationUnit(abstractDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "IEmpty" && s.Kind == SymbolKind.Abstract);
        Assert.NotNull(symbol);

        var abstractType = Assert.IsType<AbstractType>(symbol.DeclaredType);
        Assert.Equal(TypeKind.Abstract, abstractType.Kind);
        Assert.Equal("IEmpty", abstractType.DisplayName);
        Assert.Empty(abstractType.MemberScope.Members);
    }

    [Fact]
    public void AbstractWithMethods_RegistersMethodSymbolsInMemberScope()
    {
        var methods = ImmutableArray.Create(
            MakeAbstractMethod("DoWork"),
            MakeAbstractMethod("Process"),
            MakeAbstractMethod("Execute"));
        var abstractDecl = MakeAbstractDecl("IWorker", methods);
        var ast = MakeCompilationUnit(abstractDecl);
        var (model, _) = BuildModelWithDiagnostics(ast);

        var symbol = model.AllSymbols.FirstOrDefault(s => s.Name == "IWorker" && s.Kind == SymbolKind.Abstract);
        Assert.NotNull(symbol);

        var abstractType = Assert.IsType<AbstractType>(symbol.DeclaredType);
        var memberMethods = abstractType.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Method)
            .ToList();

        Assert.Equal(3, memberMethods.Count);
        Assert.Contains(memberMethods, m => m.Name == "DoWork");
        Assert.Contains(memberMethods, m => m.Name == "Process");
        Assert.Contains(memberMethods, m => m.Name == "Execute");
    }

    [Fact]
    public void MultipleAbstracts_AllRegisteredCorrectly()
    {
        var abstract1 = MakeAbstractDecl("IFoo", ImmutableArray.Create(MakeAbstractMethod("Foo")));
        var abstract2 = MakeAbstractDecl("IBar", ImmutableArray<MethodDeclarationNode>.Empty);
        var abstract3 = MakeAbstractDecl("IBaz", ImmutableArray.Create(
            MakeAbstractMethod("Baz1"),
            MakeAbstractMethod("Baz2")));

        var ast = MakeCompilationUnit(abstract1, abstract2, abstract3);
        var (model, _) = BuildModelWithDiagnostics(ast);

        // IFoo: 1 method
        var fooSymbol = model.AllSymbols.First(s => s.Name == "IFoo" && s.Kind == SymbolKind.Abstract);
        var fooType = Assert.IsType<AbstractType>(fooSymbol.DeclaredType);
        _ = Assert.Single(fooType.MemberScope.Members, m => m.Kind == SymbolKind.Method);

        // IBar: 0 methods
        var barSymbol = model.AllSymbols.First(s => s.Name == "IBar" && s.Kind == SymbolKind.Abstract);
        var barType = Assert.IsType<AbstractType>(barSymbol.DeclaredType);
        Assert.DoesNotContain(barType.MemberScope.Members, m => m.Kind == SymbolKind.Method);

        // IBaz: 2 methods
        var bazSymbol = model.AllSymbols.First(s => s.Name == "IBaz" && s.Kind == SymbolKind.Abstract);
        var bazType = Assert.IsType<AbstractType>(bazSymbol.DeclaredType);
        Assert.Equal(2, bazType.MemberScope.Members.Count(m => m.Kind == SymbolKind.Method));
    }
}