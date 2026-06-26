// Feature: class-inheritance, Property 3: Cycle detection
// **Validates: Requirements 3.2, 3.3**

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
/// Property-based tests verifying cycle detection in class inheritance.
///
/// Property 3: For any set of class declarations forming a cyclic inheritance
/// chain (including self-inheritance where a class names itself as its base),
/// the semantic model builder shall emit a diagnostic error indicating circular
/// inheritance is detected.
/// </summary>
public class CycleDetectionPropertyTests
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
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ClassDeclarationNode MakeClassDecl(string name, string? baseClassName = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        if(baseClassName is not null)
        {
            var _baseList1 = MakeBaseList(baseClassName);

            classDecl.BaseClause = _baseList1;

            classDecl.BaseList = _baseList1;
        }

        return classDecl;
    }

    private static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = types.AsTopLevelItems() };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a cyclic class set where all classes form a cycle.
    /// Each entry is (className, baseClassName).
    /// </summary>
#pragma warning disable CA1034
    public record CyclicClassSet(ImmutableArray<(string Name, string BaseName)> Classes)
    {
        public override string ToString()
        {
            var entries = Classes.Select(c => $"{c.Name} : {c.BaseName}");
            return $"[{string.Join(", ", entries)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"];

    /// <summary>
    /// Generates a self-inheritance scenario: a single class that extends itself.
    /// e.g., Alpha : Alpha
    /// </summary>
    public static Gen<CyclicClassSet> GenSelfInheritance()
    {
        return from nameIdx in Gen.Choose(0, ClassNamePool.Length - 1)
               let name = ClassNamePool[nameIdx]
               select new CyclicClassSet(ImmutableArray.Create((name, name)));
    }

    /// <summary>
    /// Generates a two-class cycle: A extends B, B extends A.
    /// </summary>
    public static Gen<CyclicClassSet> GenTwoClassCycle()
    {
        return from idx1 in Gen.Choose(0, ClassNamePool.Length - 1)
               from idx2 in Gen.Choose(0, ClassNamePool.Length - 1)
               where idx1 != idx2
               let name1 = ClassNamePool[idx1]
               let name2 = ClassNamePool[idx2]
               select new CyclicClassSet(ImmutableArray.Create(
                   (name1, name2),
                   (name2, name1)));
    }

    /// <summary>
    /// Generates a multi-class cycle of size 3-6: A→B→C→...→A.
    /// The classes are shuffled to test that cycle detection works
    /// regardless of declaration order.
    /// </summary>
    public static Gen<CyclicClassSet> GenMultiClassCycle()
    {
        return from count in Gen.Choose(3, 6)
               from shuffledNames in Gen.Shuffle(ClassNamePool).Select(names => names.Take(count).ToArray())
               from declOrder in Gen.Shuffle(Enumerable.Range(0, count).ToArray())
               select BuildMultiClassCycle(shuffledNames, declOrder.ToArray());
    }

    private static CyclicClassSet BuildMultiClassCycle(string[] names, int[] declOrder)
    {
        // Create a cycle: names[0] → names[1] → ... → names[n-1] → names[0]
        var entries = new List<(string Name, string BaseName)>();
        for(var i = 0; i < names.Length; i++)
        {
            var baseName = names[(i + 1) % names.Length];
            entries.Add((names[i], baseName));
        }

        // Reorder declarations according to declOrder to test different orderings
        var reordered = declOrder.Select(i => entries[i]).ToImmutableArray();
        return new CyclicClassSet(reordered);
    }

    /// <summary>
    /// Combines all cyclic generators: self-inheritance, two-class, and multi-class cycles.
    /// </summary>
    public static Arbitrary<CyclicClassSet> ArbitraryCyclicClassSet()
    {
        return Gen.OneOf(
            GenSelfInheritance(),
            GenTwoClassCycle(),
            GenMultiClassCycle()
        ).ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3a: For any set of class declarations forming a cyclic inheritance
    /// chain (including self-inheritance), the semantic model builder emits at least
    /// one MIST_CIRCULAR_INHERITANCE diagnostic.
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicInheritance_EmitsCircularInheritanceDiagnostic(CyclicClassSet classSet)
    {
        // Build AST from the cyclic class set
        var classDecls = classSet.Classes
            .Select(c => MakeClassDecl(c.Name, c.BaseName))
            .ToArray();
        var ast = MakeCompilationUnit(classDecls);

        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: at least one MIST_CIRCULAR_INHERITANCE diagnostic is emitted
        var cycleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_CIRCULAR_INHERITANCE")
            .ToList();

        return cycleDiags.Count > 0;
    }

    /// <summary>
    /// Property 3b: For any set of class declarations forming a cyclic inheritance
    /// chain, after cycle detection, at least one class in the cycle has its BaseType
    /// set to null (the cycle is broken).
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicInheritance_BreaksCycleBySettingBaseTypeToNull(CyclicClassSet classSet)
    {
        // Build AST from the cyclic class set
        var classDecls = classSet.Classes
            .Select(c => MakeClassDecl(c.Name, c.BaseName))
            .ToArray();
        var ast = MakeCompilationUnit(classDecls);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // Collect all class types involved in the cycle
        var classNames = new HashSet<string>(classSet.Classes.Select(c => c.Name));
        var classTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Class && classNames.Contains(s.Name))
            .Select(s => s.DeclaredType as ClassType)
            .Where(ct => ct is not null)
            .ToList();

        // At least one class in the cycle must have BaseType = null (cycle broken)
        var hasNullBaseType = classTypes.Any(ct => ct!.BaseType is null);

        return hasNullBaseType;
    }

    /// <summary>
    /// Property 3c: For any set of class declarations forming a cyclic inheritance
    /// chain, after cycle detection, no class can follow its BaseType chain
    /// indefinitely (i.e., the chain terminates — no infinite loops).
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(CycleDetectionPropertyTests) }, MaxTest = 100)]
    public bool CyclicInheritance_BaseTypeChainTerminates(CyclicClassSet classSet)
    {
        // Build AST from the cyclic class set
        var classDecls = classSet.Classes
            .Select(c => MakeClassDecl(c.Name, c.BaseName))
            .ToArray();
        var ast = MakeCompilationUnit(classDecls);

        var (model, _) = BuildModelWithDiagnostics(ast);

        // For every class, walking the BaseType chain must terminate
        var classNames = new HashSet<string>(classSet.Classes.Select(c => c.Name));
        var classTypes = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Class && classNames.Contains(s.Name))
            .Select(s => s.DeclaredType as ClassType)
            .Where(ct => ct is not null)
            .ToList();

        foreach(var classType in classTypes)
        {
            var visited = new HashSet<ClassType>();
            var current = classType;
            while(current is not null)
            {
                if(!visited.Add(current!))
                {
                    // Still a cycle — chain did not terminate
                    return false;
                }

                current = current!.BaseType as ClassType;
            }
        }

        return true;
        ;
    }

}