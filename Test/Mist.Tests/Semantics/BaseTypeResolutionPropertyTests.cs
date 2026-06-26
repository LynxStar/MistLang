// Feature: class-inheritance, Property 2: Base type resolution correctness
// **Validates: Requirements 2.1, 2.4, 2.5**

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
/// Property-based tests verifying base type resolution correctness.
///
/// Property 2: For any set of class declarations where some classes include
/// inheritance clauses referencing other declared classes (including forward
/// references), the semantic model builder shall set the BaseType property of
/// each derived class's ClassType to the correct resolved ClassType, and shall
/// set BaseType to null for classes without an inheritance clause.
/// </summary>
public class BaseTypeResolutionPropertyTests
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

    /// <summary>
    /// Creates a ClassDeclarationNode with an optional base class.
    /// When baseClassName is non-null, the BaseClass token (colon) is set
    /// and BaseClassName is set to the given identifier.
    /// </summary>
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

    /// <summary>
    /// Wraps class declarations into a CompilationUnitNode.
    /// </summary>
    private static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Items = (types).AsTopLevelItems() };
    }

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a set of class declarations for testing base type resolution.
    /// Each entry is (className, baseClassName or null).
    /// </summary>
#pragma warning disable CA1034
    public record ClassSet(ImmutableArray<(string Name, string? BaseName)> Classes)
    {
        public override string ToString()
        {
            var entries = Classes.Select(c =>
                c.BaseName is not null ? $"{c.Name} : {c.BaseName}" : c.Name);
            return $"[{string.Join(", ", entries)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Pool of unique class names to draw from.
    /// </summary>
    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"];

    /// <summary>
    /// Generates a ClassSet where some classes inherit from other classes in the set
    /// (valid references only, no undefined bases, no cycles) and some have no base class.
    /// The ordering of classes in the array is randomized to test forward references.
    /// </summary>
    public static Gen<ClassSet> GenValidClassSet()
    {
        // Pick 2-6 unique class names
        return from count in Gen.Choose(2, 6)
               from shuffledNames in Gen.Shuffle(ClassNamePool).Select(names => names.Take(count).ToArray())
               from classSet in BuildValidClassSet(shuffledNames)
               select classSet;
    }

    /// <summary>
    /// Builds a valid class set from a list of names. Each class either has no base
    /// or inherits from a class that appears earlier in the logical order (to avoid cycles).
    /// The final array order is then shuffled to test forward references.
    /// </summary>
    private static Gen<ClassSet> BuildValidClassSet(string[] names)
    {
        // For each class (except the first), decide whether it inherits from
        // one of the classes that precede it in the logical order.
        // This guarantees no cycles.
        var classEntries = new List<(string Name, string? BaseName)>();

        // First class never has a base
        classEntries.Add((names[0], null));

        // Build a generator that decides inheritance for each subsequent class
        var gen = Gen.Constant(classEntries);

        for(var i = 1; i < names.Length; i++)
        {
            var idx = i; // capture
            gen = from entries in gen
                  from hasBase in Gen.Elements(true, false)
                  from baseIdx in Gen.Choose(0, idx - 1)
                  select AddEntry(entries, names[idx], hasBase ? names[baseIdx] : null);
        }

        // Shuffle the final order to test forward references
        return from entries in gen
               from shuffled in Gen.Shuffle(entries.ToArray())
               select new ClassSet(shuffled.ToImmutableArray());
    }

    private static List<(string Name, string? BaseName)> AddEntry(
        List<(string Name, string? BaseName)> entries,
        string name,
        string? baseName)
    {
        var result = new List<(string Name, string? BaseName)>(entries) { (name, baseName) };
        return result;
    }

    /// <summary>
    /// Generates a ClassSet where at least one class references an undefined base class.
    /// </summary>
    public static Gen<ClassSet> GenClassSetWithUndefinedBase()
    {
        return from count in Gen.Choose(1, 4)
               from names in Gen.Shuffle(ClassNamePool).Select(n => n.Take(count).ToArray())
               from undefinedBase in Gen.Elements("Nonexistent", "Missing", "Unknown", "Phantom")
               from targetIdx in Gen.Choose(0, count - 1)
               select BuildClassSetWithUndefinedBase(names, undefinedBase, targetIdx);
    }

    private static ClassSet BuildClassSetWithUndefinedBase(string[] names, string undefinedBase, int targetIdx)
    {
        var entries = new List<(string Name, string? BaseName)>();
        for(var i = 0; i < names.Length; i++)
        {
            entries.Add(i == targetIdx
                ? (names[i], undefinedBase)
                : (names[i], null));
        }

        return new ClassSet(entries.ToImmutableArray());
    }

    /// <summary>FsCheck Arbitrary registration for ClassSet.</summary>
    public static Arbitrary<ClassSet> ArbitraryClassSet()
    {
        return Gen.OneOf(GenValidClassSet(), GenClassSetWithUndefinedBase()).ToArbitrary();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: For any set of class declarations with valid inheritance clauses
    /// (including forward references), the semantic model builder sets BaseType to
    /// the correct resolved ClassType for derived classes, and BaseType to null for
    /// classes without an inheritance clause.
    ///
    /// **Validates: Requirements 2.1, 2.4, 2.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BaseTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool ValidInheritance_BaseTypeIsCorrectlyResolved(ClassSet classSet)
    {
        // Filter to only valid class sets (no undefined bases)
        var definedNames = new HashSet<string>(classSet.Classes.Select(c => c.Name));
        var allBasesValid = classSet.Classes.All(c =>
            c.BaseName is null || definedNames.Contains(c.BaseName));

        if(!allBasesValid)
        {
            return true; // Skip invalid sets for this property
        }

        // Also skip if there are duplicate names (would cause registration conflicts)
        if(definedNames.Count != classSet.Classes.Length)
        {
            return true;
        }

        // Build AST
        var classDecls = classSet.Classes
            .Select(c => MakeClassDecl(c.Name, c.BaseName))
            .ToArray();
        var ast = MakeCompilationUnit(classDecls);

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No MIST_UNDEFINED_BASE_TYPE diagnostics should be emitted
        var undefinedBaseDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_BASE_TYPE")
            .ToList();
        if(undefinedBaseDiags.Count > 0)
        {
            return false;
        }

        // Verify each class's BaseType
        foreach(var (name, baseName) in classSet.Classes)
        {
            var classSymbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Class);

            if(classSymbol is null)
            {
                return false;
            }

            if(classSymbol.DeclaredType is not ClassType classType)
            {
                return false;
            }

            if(baseName is null)
            {
                // Requirement 2.4: No inheritance clause → BaseType = null
                if(classType.BaseType is not null)
                {
                    return false;
                }
            }
            else
            {
                // Requirement 2.1: Valid inheritance clause → BaseType set to correct ClassType
                if(classType.BaseType is null)
                {
                    return false;
                }

                if(classType.BaseType.DisplayName != baseName)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Property 2b: For any set of class declarations where a class references
    /// an undefined base class name, the semantic model builder emits a
    /// MIST_UNDEFINED_BASE_TYPE diagnostic and sets BaseType to null.
    ///
    /// **Validates: Requirements 2.1, 2.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BaseTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool UndefinedBase_EmitsDiagnosticAndBaseTypeIsNull(ClassSet classSet)
    {
        var definedNames = new HashSet<string>(classSet.Classes.Select(c => c.Name));

        // Only test class sets that have at least one undefined base reference
        var undefinedRefs = classSet.Classes
            .Where(c => c.BaseName is not null && !definedNames.Contains(c.BaseName))
            .ToList();

        if(undefinedRefs.Count == 0)
        {
            return true; // Skip — no undefined references to test
        }

        // Skip if there are duplicate names
        if(definedNames.Count != classSet.Classes.Length)
        {
            return true;
        }

        // Build AST
        var classDecls = classSet.Classes
            .Select(c => MakeClassDecl(c.Name, c.BaseName))
            .ToArray();
        var ast = MakeCompilationUnit(classDecls);

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // Verify MIST_UNDEFINED_BASE_TYPE diagnostic is emitted for each undefined reference
        var undefinedBaseDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_BASE_TYPE")
            .ToList();

        if(undefinedBaseDiags.Count < undefinedRefs.Count)
        {
            return false;
        }

        // Verify that classes with undefined bases have BaseType = null
        foreach(var (name, baseName) in undefinedRefs)
        {
            var classSymbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Class);

            if(classSymbol is null)
            {
                return false;
            }

            if(classSymbol.DeclaredType is not ClassType classType)
            {
                return false;
            }

            // BaseType should be null when the base is undefined
            if(classType.BaseType is not null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Represents a forward reference scenario: derived class appears before base class.
    /// </summary>
#pragma warning disable CA1034
    public record ForwardRefPair(string DerivedName, string BaseName)
    {
        public override string ToString()
        {
            return $"{DerivedName} : {BaseName} (forward ref)";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates pairs of (derived, base) class names for forward reference testing.
    /// </summary>
    public static Arbitrary<ForwardRefPair> ArbitraryForwardRefPair()
    {
        var gen = from baseIdx in Gen.Choose(0, ClassNamePool.Length - 1)
                  from derivedIdx in Gen.Choose(0, ClassNamePool.Length - 1)
                  where baseIdx != derivedIdx
                  select new ForwardRefPair(ClassNamePool[derivedIdx], ClassNamePool[baseIdx]);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 2c: Forward references are supported — a derived class can appear
    /// before its base class in the source, and BaseType is still correctly resolved.
    ///
    /// **Validates: Requirement 2.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BaseTypeResolutionPropertyTests) }, MaxTest = 100)]
    public bool ForwardReferences_BaseTypeIsCorrectlyResolved(ForwardRefPair pair)
    {
        // Derived class appears FIRST (forward reference)
        var derivedDecl = MakeClassDecl(pair.DerivedName, pair.BaseName);
        var baseDecl = MakeClassDecl(pair.BaseName);
        var ast = MakeCompilationUnit(derivedDecl, baseDecl);

        var (model, diagnostics) = BuildModelWithDiagnostics(ast);

        // No undefined base type diagnostics
        var undefinedDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_UNDEFINED_BASE_TYPE")
            .ToList();
        if(undefinedDiags.Count > 0)
        {
            return false;
        }

        // Derived class should have BaseType set to the base class
        var derivedSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == pair.DerivedName && s.Kind == SymbolKind.Class);
        if(derivedSymbol?.DeclaredType is not ClassType derivedType)
        {
            return false;
        }

        if(derivedType.BaseType is null || derivedType.BaseType.DisplayName != pair.BaseName)
        {
            return false;
        }

        // Base class should have BaseType = null
        var baseSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == pair.BaseName && s.Kind == SymbolKind.Class);
        if(baseSymbol?.DeclaredType is not ClassType baseType)
        {
            return false;
        }

        if(baseType.BaseType is not null)
        {
            return false;
        }

        return true;
        ;
    }

}