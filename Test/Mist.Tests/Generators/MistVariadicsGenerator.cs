using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class VariadicSurfaceScenario
{
    public string Source { get; }

    public string Category { get; }

    public IReadOnlyList<string> PackNames { get; }

    /// <summary>The fixed-size domain length for a <c>Type[N]:</c> pack, or <c>null</c> otherwise.</summary>
    public int? FixedArity { get; }

    /// <summary>For a use-site argument-list scenario, whether it is bracketed; <c>null</c> for any other scenario.</summary>
    public bool? IsBracketed { get; }

    public string Description { get; }

    public VariadicSurfaceScenario(
        string source,
        string category,
        IReadOnlyList<string> packNames,
        int? fixedArity,
        bool? isBracketed,
        string description)
    {
        Source = source;
        Category = category;
        PackNames = packNames;
        FixedArity = fixedArity;
        IsBracketed = isBracketed;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// The forms and their exact spellings mirror the grammar and pretty-printer surface
/// so that generated source both parses and round-trips.
/// </summary>
public static class MistVariadicsGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Bundle", "Holder", "Box", "Tup", "Row", "Pair", "Wrapper", "Bag", "Cell", "Slot"];

    private static readonly string[] PackNames =
        ["TArgs", "TMid", "TElems", "TValues", "CtorArgs", "TShapes", "TParams", "TItems"];

    private static readonly string[] ElementTypes =
        ["int", "string", "bool", "double", "long", "char", "float", "byte"];

    // Generic type names used at a use site. Deliberately excludes `Func` so that
    // the flat/bracketed argument-list forms exercise the ordinary named-type path
    // rather than the dedicated `Func` type-reference form.
    private static readonly string[] GenericTypeNames =
        ["Box", "Holder", "Bag", "Wrapper", "Container", "Pack"];

    private static readonly string[] BaseTypeNames =
        ["ICountable", "IApplier", "IContainer", "ISequence"];

    private static readonly string[] FieldNames =
        ["Item", "Slot", "Value", "Data", "Element", "Entry"];

    private static readonly string[] MethodNames =
        ["Take", "Apply", "Describe", "Forward", "Handle", "Process"];

    private static readonly string[] CalleeNames =
        ["Consume", "Sink", "Handle", "Forward", "Accept"];

    private static readonly string[] ArgNames =
        ["args", "values", "items", "rest", "elements", "parts"];

    // ─── Type-parameter pack declarations ────────────────────────────────────

    public static Gen<VariadicSurfaceScenario> GenParamsPackDecl()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"public class {typeName}<params {pack}> {{ }}"
               select new VariadicSurfaceScenario(
                   source, "params-pack", [pack], null, null,
                   $"params-pack: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenExplicitPackUnsizedDecl()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"public class {typeName}<Type[]:{pack}> {{ }}"
               select new VariadicSurfaceScenario(
                   source, "explicit-pack-unsized", [pack], null, null,
                   $"explicit-pack-unsized: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenExplicitPackSizedDecl()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               from arity in Gen.Choose(1, 4)
               let source = $"public struct {typeName}<Type[{arity}]:{pack}> {{ }}"
               select new VariadicSurfaceScenario(
                   source, "explicit-pack-sized", [pack], arity, null,
                   $"explicit-pack-sized({arity}): {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenMiddlePackDecl()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"public class {typeName}<TFirst, params {pack}, TLast> {{ }}"
               select new VariadicSurfaceScenario(
                   source, "middle-pack", [pack], null, null,
                   $"middle-pack: {source}");
    }

    // ─── Value-parameter forms ────────────────────────────────────────────────

    public static Gen<VariadicSurfaceScenario> GenValuePackParam()
    {
        return from typeName in Gen.Elements(TypeNames)
               from method in Gen.Elements(MethodNames)
               from pack in Gen.Elements(PackNames)
               from argName in Gen.Elements(ArgNames)
               let source =
                   $"public class {typeName} {{ public void {method}<params {pack}>(params {pack} {argName}) {{ }} }}"
               select new VariadicSurfaceScenario(
                   source, "value-pack", [pack], null, null,
                   $"value-pack: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenValueLevelParams()
    {
        return from typeName in Gen.Elements(TypeNames)
               from method in Gen.Elements(MethodNames)
               from elem in Gen.Elements(ElementTypes)
               from argName in Gen.Elements(ArgNames)
               let source =
                   $"public class {typeName} {{ public void {method}(params {elem}[] {argName}) {{ }} }}"
               select new VariadicSurfaceScenario(
                   source, "value-level-params", [], null, null,
                   $"value-level-params: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenPlainTupleParam()
    {
        return from typeName in Gen.Elements(TypeNames)
               from method in Gen.Elements(MethodNames)
               from pack in Gen.Elements(PackNames)
               from argName in Gen.Elements(ArgNames)
               let source =
                   $"public class {typeName} {{ public void {method}<params {pack}>(({pack}...) {argName}) {{ }} }}"
               select new VariadicSurfaceScenario(
                   source, "plain-tuple-param", [pack], null, null,
                   $"plain-tuple-param: {source}");
    }

    // ─── Type-side spreads ─────────────────────────────────────────────────────

    public static Gen<VariadicSurfaceScenario> GenTypeSpreadField()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               from field in Gen.Elements(FieldNames)
               let source =
                   $"public class {typeName}<params {pack}> {{ public {pack}... {field}; }}"
               select new VariadicSurfaceScenario(
                   source, "type-spread-field", [pack], null, null,
                   $"type-spread-field: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenTypeSpreadTypeArg()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"type {typeName}<params {pack}> = Func<{pack}..., void>;"
               select new VariadicSurfaceScenario(
                   source, "type-spread-typearg", [pack], null, null,
                   $"type-spread-typearg: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenTypeSpreadBaseType()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               from baseName in Gen.Elements(BaseTypeNames)
               from bare in Gen.Elements(true, false)
               let baseClause = bare ? $"{pack}..." : $"{baseName}<{pack}...>"
               let source = $"public class {typeName}<params {pack}> : {baseClause} {{ }}"
               select new VariadicSurfaceScenario(
                   source, "type-spread-basetype", [pack], null, null,
                   $"type-spread-basetype: {source}");
    }

    // ─── Value-side spreads ────────────────────────────────────────────────────

    public static Gen<VariadicSurfaceScenario> GenValueSpreadForward()
    {
        return from typeName in Gen.Elements(TypeNames)
               from method in Gen.Elements(MethodNames)
               from pack in Gen.Elements(PackNames)
               from callee in Gen.Elements(CalleeNames)
               from argName in Gen.Elements(ArgNames)
               let source =
                   $"public class {typeName} {{ public void {method}<params {pack}>(params {pack} {argName}) {{ {callee}({argName}...); }} }}"
               select new VariadicSurfaceScenario(
                   source, "value-spread-forward", [pack], null, null,
                   $"value-spread-forward: {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenValueSpreadZip()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               from field in Gen.Elements(FieldNames)
               from argName in Gen.Elements(ArgNames)
               let source =
                   $"public class {typeName}<params {pack}> {{ public {pack}... {field}; public {typeName}(params {pack} {argName}) {{ {field}... = {argName}...; }} }}"
               select new VariadicSurfaceScenario(
                   source, "value-spread-zip", [pack], null, null,
                   $"value-spread-zip: {source}");
    }

    // ─── Use-site argument lists ───────────────────────────────────────────────

    public static Gen<VariadicSurfaceScenario> GenFlatArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from generic in Gen.Elements(GenericTypeNames)
               from field in Gen.Elements(FieldNames)
               from count in Gen.Choose(1, 3)
               from types in Gen.ArrayOf(Gen.Elements(ElementTypes), count)
               let argList = string.Join(", ", types)
               let source =
                   $"public class {typeName} {{ public {generic}<{argList}> {field}; }}"
               select new VariadicSurfaceScenario(
                   source, "flat-args", [], null, false,
                   $"flat-args({count}): {source}");
    }

    public static Gen<VariadicSurfaceScenario> GenBracketedArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from generic in Gen.Elements(GenericTypeNames)
               from field in Gen.Elements(FieldNames)
               from count in Gen.Choose(0, 3)
               from types in Gen.ArrayOf(Gen.Elements(ElementTypes), count)
               let argList = string.Join(", ", types)
               let source =
                   $"public class {typeName} {{ public {generic}<[{argList}]> {field}; }}"
               select new VariadicSurfaceScenario(
                   source, "bracketed-args", [], count, true,
                   $"bracketed-args({count}): {source}");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    public static Gen<VariadicSurfaceScenario> GenVariadicSurface()
    {
        return Gen.OneOf(
            GenParamsPackDecl(),
            GenExplicitPackUnsizedDecl(),
            GenExplicitPackSizedDecl(),
            GenMiddlePackDecl(),
            GenValuePackParam(),
            GenValueLevelParams(),
            GenPlainTupleParam(),
            GenTypeSpreadField(),
            GenTypeSpreadTypeArg(),
            GenTypeSpreadBaseType(),
            GenValueSpreadForward(),
            GenValueSpreadZip(),
            GenFlatArgs(),
            GenBracketedArgs());
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    public static Arbitrary<VariadicSurfaceScenario> ArbitraryVariadicSurfaceScenario()
    {
        return GenVariadicSurface().ToArbitrary();
    }
}