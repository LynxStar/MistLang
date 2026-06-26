// Feature: delegate-func-conversion, Property 10: Overload resolution prefers more specific delegate type
// **Validates: Requirements 7.1, 7.2, 7.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for delegate overload resolution preference.
///
/// Property 10: For any overload set containing (a) an overload accepting DelegateType D
/// and (b) an overload accepting BaseDelegateType, when the argument is a lambda or
/// func&lt;...&gt; that is signature-compatible with D, the resolution SHALL select
/// overload (a). For any overload set containing an overload accepting DelegateType D
/// and an overload accepting the equivalent func&lt;...&gt;, when the argument is a lambda,
/// the resolution SHALL prefer the DelegateType overload.
///
/// **Validates: Requirements 7.1, 7.2, 7.4**
/// </summary>
public sealed class DelegateOverloadResolutionPropertyTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Shared type pool — simple concrete types used as type arguments.
    //  These are distinct struct types; none is assignable to another.
    // ══════════════════════════════════════════════════════════════════

    private static readonly StructType Int32Type = new("Int32", new Scope(null), "Standard");
    private static readonly StructType StringType = new("String", new Scope(null), "Standard");
    private static readonly StructType BooleanType = new("Boolean", new Scope(null), "Standard");
    private static readonly StructType DoubleType = new("Double", new Scope(null), "Standard");
    private static readonly StructType ObjectType = new("Object", new Scope(null), "Standard");

    private static readonly IType[] TypePool = [Int32Type, StringType, BooleanType, DoubleType, ObjectType];

    private static readonly SemanticModelBuilder Builder = new();

    // ══════════════════════════════════════════════════════════════════
    //  Helpers for building backend-style symbols
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a Standard.Func ClassType definition with N type parameters.
    /// </summary>
    private static ClassType MakeFuncDefinition(int arity)
    {
        var funcDef = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
        var tps = new IType[arity];
        for(var i = 0; i < arity; i++)
        {
            tps[i] = new TypeParameterType($"T{i}", i);
        }

        funcDef.TypeParameters = tps;
        return funcDef;
    }

    /// <summary>
    /// Creates a backend-style method Symbol with a single parameter of the given type.
    /// Backend symbols have pre-populated Parameters, so ParameterSignature.Extract
    /// reads the type directly without needing AST nodes.
    /// </summary>
    private static Symbol MakeBackendMethodSymbol(string name, IType parameterType)
    {
        var scope = new Scope(null);
        var methodSymbol = new Symbol(
            name: name,
            kind: SymbolKind.Method,
            origin: SymbolOrigin.Backend,
            containingScope: scope,
            declaredType: null,
            declarationSpan: null);

        var paramSymbol = new Symbol(
            name: "p0",
            kind: SymbolKind.Parameter,
            origin: SymbolOrigin.Backend,
            containingScope: scope,
            declaredType: parameterType,
            declarationSpan: null);

        methodSymbol.Parameters = new[] { paramSymbol };
        return methodSymbol;
    }

    /// <summary>
    /// Builds a minimal semantic model from an empty AST.
    /// The model is only needed to satisfy the OverloadResolver.Resolve API signature;
    /// our backend symbols have pre-populated Parameters, so ParameterSignature.Extract
    /// never queries the model for these symbols.
    /// </summary>
    private static ISemanticModel BuildMinimalModel()
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray<TypeDeclarationNode>.Empty.AsTopLevelItems(),
        };
        var diagnostics = new DiagnosticBag();
        return Builder.Build(compilation, ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast), null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Generators
    // ══════════════════════════════════════════════════════════════════

    private static Gen<IType> GenPoolType()
    {
        return Gen.Elements(TypePool);
    }

    private static Gen<string> GenIdentifier()
    {
        var startChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var tailChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        return Gen.Elements(startChars).SelectMany(start =>
            Gen.Choose(2, 8).SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(tailChars), len)
                    .Select(tail => start + new string(tail))));
    }

    private static Gen<string> GenNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => string.Join(".", segments)));
    }

    /// <summary>
    /// Generates a DelegateType with a random CLR name and func signature.
    /// The func signature has 1-5 type arguments (params + return type).
    /// </summary>
    private record DelegateScenarioInput(
        string ClrNamespace,
        string ClrSimpleName,
        IType[] FuncTypeArgs);

    private static Gen<DelegateScenarioInput> GenDelegateScenarioInput()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().SelectMany(simpleName =>
                Gen.Choose(1, 5).SelectMany(funcArity =>
                    Gen.ArrayOf(GenPoolType(), funcArity).Select(funcTypeArgs =>
                        new DelegateScenarioInput(ns, simpleName, funcTypeArgs)))));
    }

    private static DelegateType BuildDelegateType(DelegateScenarioInput input)
    {
        var funcDef = MakeFuncDefinition(input.FuncTypeArgs.Length);
        var funcSignature = new ConstructedType(funcDef, input.FuncTypeArgs);

        return new DelegateType(
            clrFullName: $"{input.ClrNamespace}.{input.ClrSimpleName}",
            clrNamespace: input.ClrNamespace,
            clrSimpleName: input.ClrSimpleName,
            funcSignature: funcSignature,
            typeParameters: Array.Empty<IType>(),
            typeArguments: Array.Empty<IType>(),
            memberScope: new Scope(null));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 10, Part A: DelegateType vs BaseDelegateType
    //
    //  For any overload set containing (a) an overload accepting DelegateType D
    //  and (b) an overload accepting BaseDelegateType, when the argument is a
    //  func<...> that is signature-compatible with D, the resolution SHALL
    //  select overload (a) — the more specific DelegateType.
    //
    //  **Validates: Requirements 7.2, 7.4**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P10_DelegateTypeVsBaseDelegateType_FuncArg_PrefersDelegateType()
    {
        var model = BuildMinimalModel();
        var samples = GenDelegateScenarioInput().Sample(100);

        foreach(var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            // Create overload set: M(DelegateType) vs M(BaseDelegateType)
            var delegateOverload = MakeBackendMethodSymbol("M", delegateType);
            var baseDelegateOverload = MakeBackendMethodSymbol("M", BaseDelegateType.Instance);

            // Argument: a func<...> type that is signature-compatible with the DelegateType.
            // Use the exact FuncSignature from the DelegateType.
            var funcArg = delegateType.FuncSignature;

            var candidates = new ISymbol[] { delegateOverload, baseDelegateOverload };
            var argTypes = new IType[] { funcArg };

            var result = OverloadResolver.Resolve(candidates, argTypes, model);

            Assert.Equal(OverloadResolutionStatus.Success, result.Status);
            Assert.Same(delegateOverload, result.BestCandidate);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 10, Part B: DelegateType vs BaseDelegateType
    //  with a DelegateType-typed argument (exact nominal match).
    //
    //  When the argument is already typed as the DelegateType, the resolver
    //  SHALL prefer the DelegateType overload over BaseDelegateType.
    //
    //  **Validates: Requirements 7.2, 7.4**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P10_DelegateTypeVsBaseDelegateType_DelegateArg_PrefersDelegateType()
    {
        var model = BuildMinimalModel();
        var samples = GenDelegateScenarioInput().Sample(100);

        foreach(var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            // Create overload set: M(DelegateType) vs M(BaseDelegateType)
            var delegateOverload = MakeBackendMethodSymbol("M", delegateType);
            var baseDelegateOverload = MakeBackendMethodSymbol("M", BaseDelegateType.Instance);

            // Argument: the DelegateType itself (exact nominal match).
            var candidates = new ISymbol[] { delegateOverload, baseDelegateOverload };
            var argTypes = new IType[] { delegateType };

            var result = OverloadResolver.Resolve(candidates, argTypes, model);

            Assert.Equal(OverloadResolutionStatus.Success, result.Status);
            Assert.Same(delegateOverload, result.BestCandidate);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 10, Part C: DelegateType vs equivalent func<...>
    //
    //  For any overload set containing an overload accepting DelegateType D
    //  and an overload accepting the equivalent func<...>, when the argument
    //  is a DelegateType-typed value, the resolution SHALL prefer the
    //  DelegateType overload (exact nominal match wins).
    //
    //  **Validates: Requirements 7.1**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P10_DelegateTypeVsFuncType_DelegateArg_PrefersDelegateType()
    {
        var model = BuildMinimalModel();
        var samples = GenDelegateScenarioInput().Sample(100);

        foreach(var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            // Create overload set: M(DelegateType) vs M(func<...>)
            var delegateOverload = MakeBackendMethodSymbol("M", delegateType);
            var funcOverload = MakeBackendMethodSymbol("M", delegateType.FuncSignature);

            // Argument: the DelegateType value itself — Rule 1 (exact nominal match).
            var candidates = new ISymbol[] { delegateOverload, funcOverload };
            var argTypes = new IType[] { delegateType };

            var result = OverloadResolver.Resolve(candidates, argTypes, model);

            Assert.Equal(OverloadResolutionStatus.Success, result.Status);
            Assert.Same(delegateOverload, result.BestCandidate);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 10, Part D: DelegateType vs equivalent func<...>
    //  with a func<...> argument.
    //
    //  When a bare func<...> value is passed, the resolver SHALL prefer
    //  the func<...> overload over the DelegateType overload.
    //
    //  **Validates: Requirements 7.1**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P10_DelegateTypeVsFuncType_FuncArg_PrefersFuncType()
    {
        var model = BuildMinimalModel();
        var samples = GenDelegateScenarioInput().Sample(100);

        foreach(var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            // Create overload set: M(DelegateType) vs M(func<...>)
            var delegateOverload = MakeBackendMethodSymbol("M", delegateType);
            var funcOverload = MakeBackendMethodSymbol("M", delegateType.FuncSignature);

            // Argument: the func<...> type (structural match).
            // Rule 2: func argument → prefer func overload.
            var funcArg = delegateType.FuncSignature;
            var candidates = new ISymbol[] { delegateOverload, funcOverload };
            var argTypes = new IType[] { funcArg };

            var result = OverloadResolver.Resolve(candidates, argTypes, model);

            Assert.Equal(OverloadResolutionStatus.Success, result.Status);
            Assert.Same(funcOverload, result.BestCandidate);
        }
    }
}