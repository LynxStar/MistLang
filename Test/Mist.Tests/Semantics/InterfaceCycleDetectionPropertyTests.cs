// Feature: interfaces, Property 5: Interface inheritance cycle detection
// **Validates: Requirements 6.1, 6.2, 6.3**

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
/// Property-based tests verifying interface inheritance cycle detection.
///
/// Property 5: For any set of interfaces that form a circular inheritance chain
/// (including self-inheritance), the semantic model builder SHALL emit a
/// MIST_CIRCULAR_INTERFACE_INHERITANCE diagnostic and the compilation SHALL
/// complete without infinite loops.
/// </summary>
public class InterfaceCycleDetectionPropertyTests
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

    private static InterfaceDeclarationNode MakeInterfaceDecl(
        string name,
        string? parentInterfaceName = null)
    {
        var decl = new InterfaceDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
        };

        if (parentInterfaceName is not null)
        {
            decl.InterfaceList = MakeToken(":");
            decl.InterfaceName = MakeToken(parentInterfaceName);
        }

        return decl;
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params InterfaceDeclarationNode[] interfaceDecls)
    {
        var types = interfaceDecls.Select(i =>
            (TypeDeclarationNode)new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = i })
            .ToImmutableArray();

        return new CompilationUnitNode { Types = types };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a set of interfaces forming a cycle.
    /// Each entry is (interfaceName, parentInterfaceName).
    /// The Storm generator only captures the first interface name after the colon,
    /// so cycles are tested with single-parent chains.
    /// </summary>
#pragma warning disable CA1034
    public record CyclicInterfaceSet(ImmutableArray<(string Name, string ParentName)> Interfaces)
    {
        public override string ToString()
        {
            var entries = Interfaces.Select(i => $"{i.Name} : {i.ParentName}");
            return $"[{string.Join(", ", entries)}]";
        }
    }

    /// <summary>
    /// Represents a set of interfaces with no cycle (a linear chain).
    /// </summary>
    public record AcyclicInterfaceSet(ImmutableArray<(string Name, string? ParentName)> Interfaces)
    {
        public override string ToString()
        {
            var entries = Interfaces.Select(i =>
                i.ParentName is not null ? $"{i.Name} : {i.ParentName}" : i.Name);
            return $"[{string.Join(", ", entries)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] InterfaceNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta", "IEta", "ITheta", "IIota", "IKappa"];

    /// <summary>
    /// Generates a self-inheritance scenario: a single interface that extends itself.
    /// e.g., IAlpha : IAlpha
    /// </summary>
    public static Gen<CyclicInterfaceSet> GenSelfInheritance()
    {
        return from nameIdx in Gen.Choose(0, InterfaceNamePool.Length - 1)
               let name = InterfaceNamePool[nameIdx]
               select new CyclicInterfaceSet(ImmutableArray.Create((name, name)));
    }

    /// <summary>
    /// Generates a two-interface cycle: IA : IB, IB : IA.
    /// </summary>
    public static Gen<CyclicInterfaceSet> GenTwoInterfaceCycle()
    {
        return from idx1 in Gen.Choose(0, InterfaceNamePool.Length - 1)
               from idx2 in Gen.Choose(0, InterfaceNamePool.Length - 1)
               where idx1 != idx2
               let name1 = InterfaceNamePool[idx1]
               let name2 = InterfaceNamePool[idx2]
               select new CyclicInterfaceSet(ImmutableArray.Create(
                   (name1, name2),
                   (name2, name1)));
    }

    /// <summary>
    /// Generates a multi-interface cycle of size 3-6: IA→IB→IC→...→IA.
    /// The interfaces are shuffled to test that cycle detection works
    /// regardless of declaration order.
    /// </summary>
    public static Gen<CyclicInterfaceSet> GenMultiInterfaceCycle()
    {
        return from count in Gen.Choose(3, 6)
               from shuffledNames in Gen.Shuffle(InterfaceNamePool).Select(names => names.Take(count).ToArray())
               from declOrder in Gen.Shuffle(Enumerable.Range(0, count).ToArray())
               select BuildMultiInterfaceCycle(shuffledNames, declOrder.ToArray());
    }

    private static CyclicInterfaceSet BuildMultiInterfaceCycle(string[] names, int[] declOrder)
    {
        // Create a cycle: names[0] → names[1] → ... → names[n-1] → names[0]
        var entries = new List<(string Name, string ParentName)>();
        for (var i = 0; i < names.Length; i++)
        {
            var parentName = names[(i + 1) % names.Length];
            entries.Add((names[i], parentName));
        }

        // Reorder declarations according to declOrder to test different orderings
        var reordered = declOrder.Select(i => entries[i]).ToImmutableArray();
        return new CyclicInterfaceSet(reordered);
    }

    /// <summary>
    /// Combines all cyclic generators: self-inheritance, two-interface, and multi-interface cycles.
    /// </summary>
    public static Arbitrary<CyclicInterfaceSet> ArbitraryCyclicInterfaceSet()
    {
        return Gen.OneOf(
            GenSelfInheritance(),
            GenTwoInterfaceCycle(),
            GenMultiInterfaceCycle()
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates an acyclic interface chain of size 2-5: IA : IB, IB : IC, ...
    /// The last interface in the chain has no parent (the root).
    /// </summary>
    public static Gen<AcyclicInterfaceSet> GenAcyclicInterfaceChain()
    {
        return from count in Gen.Choose(2, 5)
               from shuffledNames in Gen.Shuffle(InterfaceNamePool).Select(names => names.Take(count).ToArray())
               from declOrder in Gen.Shuffle(Enumerable.Range(0, count).ToArray())
               select BuildAcyclicChain(shuffledNames, declOrder.ToArray());
    }

    private static AcyclicInterfaceSet BuildAcyclicChain(string[] names, int[] declOrder)
    {
        // Create a linear chain: names[0] : names[1], names[1] : names[2], ..., names[n-1] (root, no parent)
        var entries = new List<(string Name, string? ParentName)>();
        for (var i = 0; i < names.Length; i++)
        {
            string? parentName = i < names.Length - 1 ? names[i + 1] : null;
            entries.Add((names[i], parentName));
        }

        // Reorder declarations according to declOrder
        var reordered = declOrder.Select(i => entries[i]).ToImmutableArray();
        return new AcyclicInterfaceSet(reordered);
    }

    public static Arbitrary<AcyclicInterfaceSet> ArbitraryAcyclicInterfaceSet()
    {
        return GenAcyclicInterfaceChain().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5a: For any set of interfaces forming a cyclic inheritance chain
    /// (including self-inheritance), the semantic model builder emits at least one
    /// MIST_CIRCULAR_INTERFACE_INHERITANCE diagnostic.
    ///
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicInterfaceInheritance_EmitsCircularDiagnostic(CyclicInterfaceSet interfaceSet)
    {
        var interfaceDecls = interfaceSet.Interfaces
            .Select(i => MakeInterfaceDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(interfaceDecls);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_INTERFACE_INHERITANCE")
            .ToList();

        return cycleDiags.Count > 0;
    }

    /// <summary>
    /// Property 5b: For any set of interfaces forming a cyclic inheritance chain,
    /// after cycle detection, at least one interface in the cycle has its Interfaces
    /// list emptied (the cycle is broken), ensuring no infinite loops.
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicInterfaceInheritance_BreaksCycleByRemovingBackEdge(CyclicInterfaceSet interfaceSet)
    {
        var interfaceDecls = interfaceSet.Interfaces
            .Select(i => MakeInterfaceDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(interfaceDecls);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // Collect all interface types involved in the cycle
        var interfaceNames = new HashSet<string>(interfaceSet.Interfaces.Select(i => i.Name));
        var interfaceTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Interface && interfaceNames.Contains(s.Name))
            .Select(s => s.DeclaredType as InterfaceType)
            .Where(it => it is not null)
            .ToList();

        // At least one interface in the cycle must have had its back-edge removed
        // (its Interfaces list is empty, meaning the cycle was broken)
        var hasEmptyInterfaces = interfaceTypes.Any(it => it!.Interfaces.Count == 0);

        return hasEmptyInterfaces;
    }

    /// <summary>
    /// Property 5c: For any set of interfaces forming a cyclic inheritance chain,
    /// after cycle detection, walking any interface's Interfaces chain terminates
    /// (no infinite loops).
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicInterfaceInheritance_InterfaceChainTerminates(CyclicInterfaceSet interfaceSet)
    {
        var interfaceDecls = interfaceSet.Interfaces
            .Select(i => MakeInterfaceDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(interfaceDecls);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // For every interface, walking the Interfaces chain must terminate
        var interfaceNames = new HashSet<string>(interfaceSet.Interfaces.Select(i => i.Name));
        var interfaceTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Interface && interfaceNames.Contains(s.Name))
            .Select(s => s.DeclaredType as InterfaceType)
            .Where(it => it is not null)
            .ToList();

        foreach (var interfaceType in interfaceTypes)
        {
            var visited = new HashSet<InterfaceType>();
            var queue = new Queue<InterfaceType>();
            queue.Enqueue(interfaceType!);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!visited.Add(current))
                {
                    // Still a cycle — chain did not terminate
                    return false;
                }

                foreach (var parent in current.Interfaces)
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Property 5d: For any set of interfaces that do NOT form a cycle (a linear
    /// chain), the semantic model builder SHALL NOT emit any
    /// MIST_CIRCULAR_INTERFACE_INHERITANCE diagnostics.
    ///
    /// **Validates: Requirements 6.1, 6.2** (negative case)
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InterfaceCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool AcyclicInterfaceInheritance_EmitsNoCycleDiagnostic(AcyclicInterfaceSet interfaceSet)
    {
        var interfaceDecls = interfaceSet.Interfaces
            .Select(i => MakeInterfaceDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(interfaceDecls);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_INTERFACE_INHERITANCE")
            .ToList();

        return cycleDiags.Count == 0;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void SelfInheritance_EmitsCircularDiagnostic()
    {
        var interfaceDecl = MakeInterfaceDecl("IFoo", "IFoo");
        var ast = MakeCompilationUnit(interfaceDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_CIRCULAR_INTERFACE_INHERITANCE");
        Assert.Contains("IFoo", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void TwoStepCycle_EmitsCircularDiagnostic()
    {
        var ifoo = MakeInterfaceDecl("IFoo", "IBar");
        var ibar = MakeInterfaceDecl("IBar", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_INTERFACE_INHERITANCE")
            .ToList();
        Assert.True(cycleDiags.Count > 0);
    }

    [Fact]
    public void ThreeStepCycle_EmitsCircularDiagnostic()
    {
        var ifoo = MakeInterfaceDecl("IFoo", "IBar");
        var ibar = MakeInterfaceDecl("IBar", "IBaz");
        var ibaz = MakeInterfaceDecl("IBaz", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar, ibaz);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_INTERFACE_INHERITANCE")
            .ToList();
        Assert.True(cycleDiags.Count > 0);
    }

    [Fact]
    public void NoCycle_EmitsNoDiagnostic()
    {
        var ifoo = MakeInterfaceDecl("IFoo");
        var ibar = MakeInterfaceDecl("IBar", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar);

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_CIRCULAR_INTERFACE_INHERITANCE");

        // Verify the chain is intact
        var ibarSymbol = model.AllSymbols.First(s => s.Name == "IBar" && s.Kind == SymbolKind.Interface);
        var ibarType = Assert.IsType<InterfaceType>(ibarSymbol.DeclaredType);
        Assert.Single(ibarType.Interfaces);
        Assert.Equal("IFoo", ibarType.Interfaces[0].DisplayName);
    }

    [Fact]
    public void SelfInheritance_BreaksCycle()
    {
        var interfaceDecl = MakeInterfaceDecl("IFoo", "IFoo");
        var ast = MakeCompilationUnit(interfaceDecl);

        var (model, _) = BuildModelWithDiagnostics(ast);

        var ifooSymbol = model.AllSymbols.First(s => s.Name == "IFoo" && s.Kind == SymbolKind.Interface);
        var ifooType = Assert.IsType<InterfaceType>(ifooSymbol.DeclaredType);

        // The self-reference should be removed
        Assert.Empty(ifooType.Interfaces);
    }

    [Fact]
    public void ThreeStepCycle_ChainTerminatesAfterBreaking()
    {
        var ifoo = MakeInterfaceDecl("IFoo", "IBar");
        var ibar = MakeInterfaceDecl("IBar", "IBaz");
        var ibaz = MakeInterfaceDecl("IBaz", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar, ibaz);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // Walking any interface's chain must terminate
        var interfaceTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Interface)
            .Select(s => s.DeclaredType as InterfaceType)
            .Where(it => it is not null)
            .ToList();

        foreach (var interfaceType in interfaceTypes)
        {
            var visited = new HashSet<InterfaceType>();
            var queue = new Queue<InterfaceType>();
            queue.Enqueue(interfaceType!);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                Assert.True(visited.Add(current), $"Cycle still present at {current.DisplayName}");
                foreach (var parent in current.Interfaces)
                {
                    queue.Enqueue(parent);
                }
            }
        }
    }
}
