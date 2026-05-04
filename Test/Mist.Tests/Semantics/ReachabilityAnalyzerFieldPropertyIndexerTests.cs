// Feature: unknown-type (Phase C, Task 17.5)
// **Validates: Requirements 9.2, 9.3, 9.4, 9.11**
//
// Unit tests for ReachabilityAnalyzer's field, property, and indexer
// classification. These tests construct the in-memory type model directly —
// ClassType, field Symbol, PropertyInfo — and invoke Analyze() without going
// through compilation. This is the approach suggested in the task description
// and matches the style used by TypeContainmentTests.
//
// Method and constructor classification is a Task 18 concern and is not
// exercised here beyond confirming the interface is total (i.e., a method
// lookup does not throw).

using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Storm.Runtime;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tests for <see cref="ReachabilityAnalyzer.Analyze"/>, specifically its
/// handling of fields, properties, and indexers under the Task 17 static
/// (non-body-walk) classification rules.
/// </summary>
public class ReachabilityAnalyzerFieldPropertyIndexerTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Helper: build a fresh generic class with one type parameter <c>T</c>,
    // register a SymbolTable entry for it, and return the type + parameter.
    //
    // The SyntaxNode parameter to SymbolTable.Register is a sentinel
    // constructed via Unsafe; the analyzer does not dereference it in Task 17,
    // so a SENTINEL placeholder is sufficient. Callers add members by creating
    // Symbols attached to the class's MemberScope (via Scope.AddMember, which
    // is internal but visible to Mist.Tests via InternalsVisibleTo).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A sentinel SyntaxNode used as the declaring-node argument when
    /// registering symbols in tests. The analyzer never inspects this node
    /// for Task 17 static classifications, so an empty node is sufficient.
    /// </summary>
    private sealed class SentinelNode : SyntaxNode
    {
        public override IEnumerable<SyntaxNode> Children() => Array.Empty<SyntaxNode>();
    }

    private static readonly SentinelNode Sentinel = new();

    /// <summary>
    /// Constructs a generic class <c>Name&lt;T&gt;</c>, registers its
    /// type-declaration symbol on the given <see cref="SymbolTable"/>, and
    /// returns the <see cref="ClassType"/>, its sole type parameter, and the
    /// type symbol. The class's <see cref="ClassType.MemberScope"/> is empty
    /// — callers add members explicitly.
    /// </summary>
    private static (ClassType ClassType, TypeParameterType T, Symbol TypeSymbol) BuildGenericClass(
        string name,
        SymbolTable symbolTable,
        Scope? fileScope = null)
    {
        var memberScope = new Scope(parent: fileScope);
        var classType = new ClassType(name, memberScope, containingNamespace: "TestNs");
        var typeParameter = new TypeParameterType("T", 0);
        classType.TypeParameters = new[] { typeParameter };

        var typeSymbol = new Symbol(
            name: name,
            kind: SymbolKind.Class,
            origin: SymbolOrigin.UserCode,
            containingScope: fileScope ?? new Scope(parent: null),
            declaredType: classType,
            declarationSpan: null);
        symbolTable.Register(typeSymbol, Sentinel);

        return (classType, typeParameter, typeSymbol);
    }

    /// <summary>
    /// Constructs a field symbol of the given declared type and registers it
    /// on both the class's member scope and the symbol table. Uses a fresh
    /// sentinel SyntaxNode per call so multiple fields can coexist in the
    /// same SymbolTable without colliding.
    /// </summary>
    private static Symbol AddField(ClassType classType, string fieldName, IType fieldType, SymbolTable symbolTable)
    {
        var scope = (Scope)classType.MemberScope;
        var fieldSymbol = new Symbol(
            name: fieldName,
            kind: SymbolKind.Field,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: fieldType,
            declarationSpan: null);
        scope.AddMember(fieldSymbol);
        symbolTable.Register(fieldSymbol, new SentinelNode());
        return fieldSymbol;
    }

    /// <summary>
    /// Adds an auto-implemented property <c>T Name { get; set; }</c>-style
    /// to the class. The PropertyInfo has both accessors marked as
    /// <see cref="AccessorBodyKind.Auto"/>, which is what the analyzer uses
    /// to decide that the setter writes a compiler-generated T-typed backing
    /// field.
    /// </summary>
    private static Symbol AddAutoProperty(ClassType classType, string propertyName, IType propertyType, SymbolTable symbolTable)
    {
        var scope = (Scope)classType.MemberScope;
        var propSymbol = new Symbol(
            name: propertyName,
            kind: SymbolKind.Property,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: propertyType,
            declarationSpan: null);
        propSymbol.PropertyMetadata = new PropertyInfo
        {
            HasGet = true,
            HasSet = true,
            HasInit = false,
            IsComputed = false,
            IsStatic = false,
            IsExpressionBodied = false,
            GetAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto },
            SetAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto },
            InitAccessor = null,
            BackingField = null,
        };
        scope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, new SentinelNode());
        return propSymbol;
    }

    /// <summary>
    /// Adds a block-body property <c>Name { get { ... } set { ... } }</c>
    /// with explicit accessor bodies. Task 17 classifies such properties as
    /// <see cref="MemberClassification.DoesNotWriteT"/> on both read and
    /// write pending Task 18's body walk.
    /// </summary>
    private static Symbol AddBlockProperty(ClassType classType, string propertyName, IType propertyType, SymbolTable symbolTable)
    {
        var scope = (Scope)classType.MemberScope;
        var propSymbol = new Symbol(
            name: propertyName,
            kind: SymbolKind.Property,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: propertyType,
            declarationSpan: null);
        propSymbol.PropertyMetadata = new PropertyInfo
        {
            HasGet = true,
            HasSet = true,
            HasInit = false,
            IsComputed = false,
            IsStatic = false,
            IsExpressionBodied = false,
            GetAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Block },
            SetAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Block },
            InitAccessor = null,
            BackingField = null,
        };
        scope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, new SentinelNode());
        return propSymbol;
    }

    /// <summary>
    /// Adds an auto-implemented indexer <c>this[int i] { get; set; }</c>-style
    /// whose element type is <paramref name="elementType"/>. Indexers share
    /// PropertyInfo with properties in this semantic model, so the
    /// construction is identical to <see cref="AddAutoProperty"/> except for
    /// <see cref="ISymbol.Kind"/>.
    /// </summary>
    private static Symbol AddAutoIndexer(ClassType classType, IType elementType, SymbolTable symbolTable)
    {
        var scope = (Scope)classType.MemberScope;
        var indexerSymbol = new Symbol(
            name: "this[]",
            kind: SymbolKind.Indexer,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: elementType,
            declarationSpan: null);
        indexerSymbol.PropertyMetadata = new PropertyInfo
        {
            HasGet = true,
            HasSet = true,
            HasInit = false,
            IsComputed = false,
            IsStatic = false,
            IsExpressionBodied = false,
            GetAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto },
            SetAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto },
            InitAccessor = null,
            BackingField = null,
        };
        scope.AddMember(indexerSymbol);
        symbolTable.Register(indexerSymbol, new SentinelNode());
        return indexerSymbol;
    }

    /// <summary>
    /// Empty expression-type map used for all Task 17 tests. The analyzer
    /// does not consult it for static classifications.
    /// </summary>
    private static readonly IReadOnlyDictionary<SyntaxNode, IType> NoExpressionTypes =
        new Dictionary<SyntaxNode, IType>();

    // ═══════════════════════════════════════════════════════════════════════
    // Field classification — Requirement 9.2.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>class List&lt;T&gt; { T[] items; }</c> — the canonical Task 17.2
    /// case. The field's declared type is <c>T[]</c>, which contains T at a
    /// storage position via the array element, so a write to <c>items</c>
    /// writes T. Reads are always <see cref="MemberClassification.DoesNotWriteT"/>
    /// (not represented in the interface; implicitly true).
    /// </summary>
    [Fact]
    public void Field_Of_ArrayOfT_Write_Is_WritesT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("List", symbols);
        var itemsField = AddField(classType, "items", new ArrayType(T), symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);

        Assert.Equal(MemberClassification.WritesT, map.GetFieldClassification(itemsField, T));
    }

    /// <summary>
    /// <c>class Box&lt;T&gt; { int tag; }</c> — a field whose declared type
    /// does not mention T writes a non-T slot, so the write classification is
    /// <see cref="MemberClassification.DoesNotWriteT"/>. This is the
    /// negative-space counterpart of the previous test.
    /// </summary>
    [Fact]
    public void Field_Of_NonTType_Write_Is_DoesNotWriteT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Box", symbols);
        var tagField = AddField(classType, "tag", LegacyPrimitiveTypes.Int, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);

        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetFieldClassification(tagField, T));
    }

    /// <summary>
    /// <c>class Cell&lt;T&gt; { T value; }</c> — a field whose declared type
    /// <i>is</i> T writes T directly. Covers the simplest storage-position
    /// containment case where the needle is the haystack.
    /// </summary>
    [Fact]
    public void Field_Of_BareT_Write_Is_WritesT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Cell", symbols);
        var valueField = AddField(classType, "value", T, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);

        Assert.Equal(MemberClassification.WritesT, map.GetFieldClassification(valueField, T));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property classification — Requirements 9.3, 9.11.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-property <c>T Value { get; set; }</c> — the canonical Task 17.3
    /// case. The compiler-generated backing field is T-typed, so the setter
    /// writes T; the getter only reads the backing field, so it does not
    /// write T. Covers both conjuncts of Requirement 9.11.
    /// </summary>
    [Fact]
    public void AutoProperty_Of_T_Has_Read_DoesNotWriteT_And_Write_WritesT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Holder", symbols);
        var valueProp = AddAutoProperty(classType, "Value", T, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);
        var classification = map.GetPropertyClassification(valueProp, T);

        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Read);
        Assert.Equal(MemberClassification.WritesT, classification.Write);
    }

    /// <summary>
    /// Auto-property <c>int Count { get; set; }</c> — when the declared type
    /// does not mention T, the generated backing field is not T-typed, so
    /// the write is <see cref="MemberClassification.DoesNotWriteT"/>.
    /// </summary>
    [Fact]
    public void AutoProperty_Of_NonTType_Has_Both_DoesNotWriteT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Holder", symbols);
        var countProp = AddAutoProperty(classType, "Count", LegacyPrimitiveTypes.Int, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);
        var classification = map.GetPropertyClassification(countProp, T);

        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Read);
        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Write);
    }

    /// <summary>
    /// Block-body property <c>T Value { get { ... } set { ... } }</c> —
    /// Task 17.3 stub case: explicit accessor bodies leave the real
    /// classification to Task 18's body walk. Until then, both read and
    /// write are conservatively <see cref="MemberClassification.DoesNotWriteT"/>,
    /// which is the lattice bottom and the correct under-approximation for
    /// fixed-point iteration.
    /// </summary>
    [Fact]
    public void BlockBodyProperty_Defaults_To_DoesNotWriteT_For_Both_Read_And_Write()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Holder", symbols);
        var valueProp = AddBlockProperty(classType, "Value", T, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);
        var classification = map.GetPropertyClassification(valueProp, T);

        // Task 17 default — Task 18 may promote Write to WritesT if the
        // setter body turns out to assign to T-typed storage. For now,
        // both are conservatively DoesNotWriteT.
        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Read);
        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Write);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Indexer classification — Requirement 9.4, 9.11 (Task 17.4).
    //
    // Indexers share PropertyInfo and the auto vs. block distinction with
    // properties, so behavior is parallel.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-indexer <c>T this[int i] { get; set; }</c> — analogous to the
    /// auto-property case. Writes are <see cref="MemberClassification.WritesT"/>
    /// because the compiler-generated backing is T-shaped (conceptually
    /// equivalent to a <c>T[]</c> store); reads do not write T.
    /// </summary>
    [Fact]
    public void AutoIndexer_Of_T_Has_Read_DoesNotWriteT_And_Write_WritesT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Vec", symbols);
        var indexer = AddAutoIndexer(classType, T, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);
        var classification = map.GetPropertyClassification(indexer, T);

        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Read);
        Assert.Equal(MemberClassification.WritesT, classification.Write);
    }

    /// <summary>
    /// Auto-indexer <c>T this[int i]</c> whose element type is <c>T[]</c> —
    /// the bullet point from Task 17.5's "indexer that stores into T[]" case.
    /// Storage-position containment descends through ArrayType, so the
    /// indexer element is T-shaped and the write is classified as
    /// <see cref="MemberClassification.WritesT"/>.
    /// </summary>
    [Fact]
    public void AutoIndexer_Of_ArrayOfT_Has_Write_WritesT_And_Read_DoesNotWriteT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Jagged", symbols);
        var indexer = AddAutoIndexer(classType, new ArrayType(T), symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);
        var classification = map.GetPropertyClassification(indexer, T);

        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Read);
        Assert.Equal(MemberClassification.WritesT, classification.Write);
    }

    /// <summary>
    /// Auto-indexer <c>int this[string key] { get; set; }</c> — the element
    /// type does not contain T, so both read and write are
    /// <see cref="MemberClassification.DoesNotWriteT"/>. Negative-space
    /// confirmation that indexer classification is driven by the element
    /// type, not by the indexer key or the mere presence of an auto setter.
    /// </summary>
    [Fact]
    public void AutoIndexer_Of_NonTType_Has_Both_DoesNotWriteT()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Table", symbols);
        var indexer = AddAutoIndexer(classType, LegacyPrimitiveTypes.Int, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);
        var classification = map.GetPropertyClassification(indexer, T);

        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Read);
        Assert.Equal(MemberClassification.DoesNotWriteT, classification.Write);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Interface total-ness — the map must provide a classification for every
    // member of every generic type, including non-classified kinds (methods,
    // constructors) that Task 17 defaults to DoesNotWriteT. This confirms
    // the Task 17 placeholder is present and that Task 18 has a stable entry
    // to refine.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The map is total over methods even before Task 18 wires up the body
    /// walk — a method lookup returns the bottom classification,
    /// <see cref="MemberClassification.DoesNotWriteT"/>. Confirms the Task 17
    /// placeholder for methods is recorded.
    /// </summary>
    [Fact]
    public void Method_Defaults_To_DoesNotWriteT_Pending_Task18()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Box", symbols);
        var scope = (Scope)classType.MemberScope;
        var method = new Symbol(
            name: "Get",
            kind: SymbolKind.Method,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: T, // return type; not used by Task 17.
            declarationSpan: null);
        scope.AddMember(method);
        symbols.Register(method, new SentinelNode());

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);

        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(method, T));
    }

    /// <summary>
    /// Non-generic types produce no classifications — the map throws on any
    /// lookup targeting their members. A class with zero type parameters has
    /// no T to classify against, matching the design's "non-generic types
    /// have nothing to classify" rule.
    /// </summary>
    [Fact]
    public void NonGenericType_Produces_No_Entries()
    {
        var symbols = new SymbolTable();

        // Build a non-generic class directly, bypassing BuildGenericClass.
        var memberScope = new Scope(parent: null);
        var plainClass = new ClassType("Plain", memberScope, containingNamespace: "TestNs");
        var typeSymbol = new Symbol(
            name: "Plain",
            kind: SymbolKind.Class,
            origin: SymbolOrigin.UserCode,
            containingScope: new Scope(parent: null),
            declaredType: plainClass,
            declarationSpan: null);
        symbols.Register(typeSymbol, Sentinel);

        var field = AddField(plainClass, "x", LegacyPrimitiveTypes.Int, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);

        // Any type parameter passed here is necessarily a stranger to `Plain`.
        var stranger = new TypeParameterType("T", 0);
        Assert.Throws<KeyNotFoundException>(
            () => map.GetFieldClassification(field, stranger));
    }

    /// <summary>
    /// <c>Lookup</c> must throw <see cref="ArgumentException"/> when the
    /// caller passes a symbol whose kind does not match the getter — e.g.,
    /// passing a field symbol to <see cref="IReachabilityMap.GetMethodClassification"/>.
    /// This guards against silent misuse at the Type Checker integration
    /// site (Task 21).
    /// </summary>
    [Fact]
    public void GetMethodClassification_On_FieldSymbol_Throws()
    {
        var symbols = new SymbolTable();
        var (classType, T, _) = BuildGenericClass("Box", symbols);
        var field = AddField(classType, "x", T, symbols);

        var map = ReachabilityAnalyzer.Analyze(symbols, NoExpressionTypes);

        Assert.Throws<ArgumentException>(
            () => map.GetMethodClassification(field, T));
    }
}
