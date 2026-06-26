// Feature: abstracts, Property 5: Abstract inheritance cycle detection
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
/// Property-based tests verifying abstract inheritance cycle detection.
///
/// Property 5: For any set of abstracts that form a circular inheritance chain
/// (including self-inheritance), the semantic model builder SHALL emit a
/// MIST_CIRCULAR_ABSTRACT_INHERITANCE diagnostic and the compilation SHALL
/// complete without infinite loops.
/// </summary>
public class AbstractCycleDetectionPropertyTests
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

    private static AbstractDeclarationNode MakeAbstractDecl(
        string name,
        string? parentAbstractName = null)
    {
        var decl = new AbstractDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        };

        if(parentAbstractName is not null)
        {
            var _abstractList1 = MakeBaseList(parentAbstractName);

            decl.AbstractList = _abstractList1;

            decl.Abstracts = _abstractList1;
        }

        return decl;
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params AbstractDeclarationNode[] abstractDecls)
    {
        var types = abstractDecls.Select(i =>
            (TypeDeclarationNode)new AbstractTypeTypeDeclarationNode { AbstractDeclaration = i })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = (types).AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a set of abstracts forming a cycle.
    /// Each entry is (abstractName, parentAbstractName).
    /// The Storm generator only captures the first abstract name after the colon,
    /// so cycles are tested with single-parent chains.
    /// </summary>
#pragma warning disable CA1034
    public record CyclicAbstractSet(ImmutableArray<(string Name, string ParentName)> Abstracts)
    {
        public override string ToString()
        {
            var entries = Abstracts.Select(i => $"{i.Name} : {i.ParentName}");
            return $"[{string.Join(", ", entries)}]";
        }
    }

    /// <summary>
    /// Represents a set of abstracts with no cycle (a linear chain).
    /// </summary>
    public record AcyclicAbstractSet(ImmutableArray<(string Name, string? ParentName)> Abstracts)
    {
        public override string ToString()
        {
            var entries = Abstracts.Select(i =>
                i.ParentName is not null ? $"{i.Name} : {i.ParentName}" : i.Name);
            return $"[{string.Join(", ", entries)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNamePool =
        ["IAlpha", "IBeta", "IGamma", "IDelta", "IEpsilon", "IZeta", "IEta", "ITheta", "IIota", "IKappa"];

    /// <summary>
    /// Generates a self-inheritance scenario: a single abstract that extends itself.
    /// e.g., IAlpha : IAlpha
    /// </summary>
    public static Gen<CyclicAbstractSet> GenSelfInheritance()
    {
        return from nameIdx in Gen.Choose(0, AbstractNamePool.Length - 1)
               let name = AbstractNamePool[nameIdx]
               select new CyclicAbstractSet(ImmutableArray.Create((name, name)));
    }

    /// <summary>
    /// Generates a two-abstract cycle: IA : IB, IB : IA.
    /// </summary>
    public static Gen<CyclicAbstractSet> GenTwoAbstractCycle()
    {
        return from idx1 in Gen.Choose(0, AbstractNamePool.Length - 1)
               from idx2 in Gen.Choose(0, AbstractNamePool.Length - 1)
               where idx1 != idx2
               let name1 = AbstractNamePool[idx1]
               let name2 = AbstractNamePool[idx2]
               select new CyclicAbstractSet(ImmutableArray.Create(
                   (name1, name2),
                   (name2, name1)));
    }

    /// <summary>
    /// Generates a multi-abstract cycle of size 3-6: IA→IB→IC→...→IA.
    /// The abstracts are shuffled to test that cycle detection works
    /// regardless of declaration order.
    /// </summary>
    public static Gen<CyclicAbstractSet> GenMultiAbstractCycle()
    {
        return from count in Gen.Choose(3, 6)
               from shuffledNames in Gen.Shuffle(AbstractNamePool).Select(names => names.Take(count).ToArray())
               from declOrder in Gen.Shuffle(Enumerable.Range(0, count).ToArray())
               select BuildMultiAbstractCycle(shuffledNames, declOrder.ToArray());
    }

    private static CyclicAbstractSet BuildMultiAbstractCycle(string[] names, int[] declOrder)
    {
        // Create a cycle: names[0] → names[1] → ... → names[n-1] → names[0]
        var entries = new List<(string Name, string ParentName)>();
        for(var i = 0; i < names.Length; i++)
        {
            var parentName = names[(i + 1) % names.Length];
            entries.Add((names[i], parentName));
        }

        // Reorder declarations according to declOrder to test different orderings
        var reordered = declOrder.Select(i => entries[i]).ToImmutableArray();
        return new CyclicAbstractSet(reordered);
    }

    /// <summary>
    /// Combines all cyclic generators: self-inheritance, two-abstract, and multi-abstract cycles.
    /// </summary>
    public static Arbitrary<CyclicAbstractSet> ArbitraryCyclicAbstractSet()
    {
        return Gen.OneOf(
            GenSelfInheritance(),
            GenTwoAbstractCycle(),
            GenMultiAbstractCycle()
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates an acyclic abstract chain of size 2-5: IA : IB, IB : IC, ...
    /// The last abstract in the chain has no parent (the root).
    /// </summary>
    public static Gen<AcyclicAbstractSet> GenAcyclicAbstractChain()
    {
        return from count in Gen.Choose(2, 5)
               from shuffledNames in Gen.Shuffle(AbstractNamePool).Select(names => names.Take(count).ToArray())
               from declOrder in Gen.Shuffle(Enumerable.Range(0, count).ToArray())
               select BuildAcyclicChain(shuffledNames, declOrder.ToArray());
    }

    private static AcyclicAbstractSet BuildAcyclicChain(string[] names, int[] declOrder)
    {
        // Create a linear chain: names[0] : names[1], names[1] : names[2], ..., names[n-1] (root, no parent)
        var entries = new List<(string Name, string? ParentName)>();
        for(var i = 0; i < names.Length; i++)
        {
            string? parentName = i < names.Length - 1 ? names[i + 1] : null;
            entries.Add((names[i], parentName));
        }

        // Reorder declarations according to declOrder
        var reordered = declOrder.Select(i => entries[i]).ToImmutableArray();
        return new AcyclicAbstractSet(reordered);
    }

    public static Arbitrary<AcyclicAbstractSet> ArbitraryAcyclicAbstractSet()
    {
        return GenAcyclicAbstractChain().ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5a: For any set of abstracts forming a cyclic inheritance chain
    /// (including self-inheritance), the semantic model builder emits at least one
    /// MIST_CIRCULAR_ABSTRACT_INHERITANCE diagnostic.
    ///
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicAbstractInheritance_EmitsCircularDiagnostic(CyclicAbstractSet abstractSet)
    {
        var abstractDecls = abstractSet.Abstracts
            .Select(i => MakeAbstractDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(abstractDecls);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_ABSTRACT_INHERITANCE")
            .ToList();

        return cycleDiags.Count > 0;
    }

    /// <summary>
    /// Property 5b: For any set of abstracts forming a cyclic inheritance chain,
    /// after cycle detection, at least one abstract in the cycle has its Abstracts
    /// list emptied (the cycle is broken), ensuring no infinite loops.
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicAbstractInheritance_BreaksCycleByRemovingBackEdge(CyclicAbstractSet abstractSet)
    {
        var abstractDecls = abstractSet.Abstracts
            .Select(i => MakeAbstractDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(abstractDecls);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // Collect all abstract types involved in the cycle
        var abstractNames = new HashSet<string>(abstractSet.Abstracts.Select(i => i.Name));
        var abstractTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Abstract && abstractNames.Contains(s.Name))
            .Select(s => s.DeclaredType as AbstractType)
            .Where(it => it is not null)
            .ToList();

        // At least one abstract in the cycle must have had its back-edge removed
        // (its Abstracts list is empty, meaning the cycle was broken)
        var hasEmptyAbstracts = abstractTypes.Any(it => it!.Abstracts.Count == 0);

        return hasEmptyAbstracts;
    }

    /// <summary>
    /// Property 5c: For any set of abstracts forming a cyclic inheritance chain,
    /// after cycle detection, walking any abstract's Abstracts chain terminates
    /// (no infinite loops).
    ///
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicAbstractInheritance_AbstractChainTerminates(CyclicAbstractSet abstractSet)
    {
        var abstractDecls = abstractSet.Abstracts
            .Select(i => MakeAbstractDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(abstractDecls);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // For every abstract, walking the Abstracts chain must terminate
        var abstractNames = new HashSet<string>(abstractSet.Abstracts.Select(i => i.Name));
        var abstractTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Abstract && abstractNames.Contains(s.Name))
            .Select(s => s.DeclaredType as AbstractType)
            .Where(it => it is not null)
            .ToList();

        foreach(var abstractType in abstractTypes)
        {
            var visited = new HashSet<AbstractType>();
            var queue = new Queue<AbstractType>();
            queue.Enqueue(abstractType!);

            while(queue.Count > 0)
            {
                var current = queue.Dequeue();
                if(!visited.Add(current))
                {
                    // Still a cycle — chain did not terminate
                    return false;
                }

                foreach(var parent in current.Abstracts.OfType<AbstractType>())
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Property 5d: For any set of abstracts that do NOT form a cycle (a linear
    /// chain), the semantic model builder SHALL NOT emit any
    /// MIST_CIRCULAR_ABSTRACT_INHERITANCE diagnostics.
    ///
    /// **Validates: Requirements 6.1, 6.2** (negative case)
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AbstractCycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool AcyclicAbstractInheritance_EmitsNoCycleDiagnostic(AcyclicAbstractSet abstractSet)
    {
        var abstractDecls = abstractSet.Abstracts
            .Select(i => MakeAbstractDecl(i.Name, i.ParentName))
            .ToArray();
        var ast = MakeCompilationUnit(abstractDecls);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_ABSTRACT_INHERITANCE")
            .ToList();

        return cycleDiags.Count == 0;
    }

    // ─── Targeted unit tests ─────────────────────────────────────────────

    [Fact]
    public void SelfInheritance_EmitsCircularDiagnostic()
    {
        var abstractDecl = MakeAbstractDecl("IFoo", "IFoo");
        var ast = MakeCompilationUnit(abstractDecl);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_CIRCULAR_ABSTRACT_INHERITANCE");
        Assert.Contains("IFoo", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void TwoStepCycle_EmitsCircularDiagnostic()
    {
        var ifoo = MakeAbstractDecl("IFoo", "IBar");
        var ibar = MakeAbstractDecl("IBar", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_ABSTRACT_INHERITANCE")
            .ToList();
        Assert.True(cycleDiags.Count > 0);
    }

    [Fact]
    public void ThreeStepCycle_EmitsCircularDiagnostic()
    {
        var ifoo = MakeAbstractDecl("IFoo", "IBar");
        var ibar = MakeAbstractDecl("IBar", "IBaz");
        var ibaz = MakeAbstractDecl("IBaz", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar, ibaz);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_ABSTRACT_INHERITANCE")
            .ToList();
        Assert.True(cycleDiags.Count > 0);
    }

    [Fact]
    public void NoCycle_EmitsNoDiagnostic()
    {
        var ifoo = MakeAbstractDecl("IFoo");
        var ibar = MakeAbstractDecl("IBar", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar);

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_CIRCULAR_ABSTRACT_INHERITANCE");

        // Verify the chain is intact
        var ibarSymbol = model.AllSymbols.First(s => s.Name == "IBar" && s.Kind == SymbolKind.Abstract);
        var ibarType = Assert.IsType<AbstractType>(ibarSymbol.DeclaredType);
        Assert.Single(ibarType.Abstracts);
        Assert.Equal("IFoo", ibarType.Abstracts[0].DisplayName);
    }

    [Fact]
    public void SelfInheritance_BreaksCycle()
    {
        var abstractDecl = MakeAbstractDecl("IFoo", "IFoo");
        var ast = MakeCompilationUnit(abstractDecl);

        var (model, _) = BuildModelWithDiagnostics(ast);

        var ifooSymbol = model.AllSymbols.First(s => s.Name == "IFoo" && s.Kind == SymbolKind.Abstract);
        var ifooType = Assert.IsType<AbstractType>(ifooSymbol.DeclaredType);

        // The self-reference should be removed
        Assert.Empty(ifooType.Abstracts);
    }

    [Fact]
    public void ThreeStepCycle_ChainTerminatesAfterBreaking()
    {
        var ifoo = MakeAbstractDecl("IFoo", "IBar");
        var ibar = MakeAbstractDecl("IBar", "IBaz");
        var ibaz = MakeAbstractDecl("IBaz", "IFoo");
        var ast = MakeCompilationUnit(ifoo, ibar, ibaz);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // Walking any abstract's chain must terminate
        var abstractTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Abstract)
            .Select(s => s.DeclaredType as AbstractType)
            .Where(it => it is not null)
            .ToList();

        foreach(var abstractType in abstractTypes)
        {
            var visited = new HashSet<AbstractType>();
            var queue = new Queue<AbstractType>();
            queue.Enqueue(abstractType!);

            while(queue.Count > 0)
            {
                var current = queue.Dequeue();
                Assert.True(visited.Add(current), $"Cycle still present at {current.DisplayName}");
                foreach(var parent in current.Abstracts.OfType<AbstractType>())
                {
                    queue.Enqueue(parent);
                }
            }
        }
    ;
    }

}