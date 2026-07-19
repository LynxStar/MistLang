// Generators for Mist variadic surface forms.
// Produces random but structurally valid Mist source text exercising every variadic
// surface form introduced by the variadics feature, for property-based testing.
// Feature: variadics, task 1.3 (shared in-process fixture and FsCheck generators).
// **Validates: Requirements 2.6**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated variadic surface-form scenario. Carries a complete,
/// parseable Mist declaration together with structural information about which
/// surface form was generated, so that property and unit tests can verify AST
/// fields (pack names, fixed-size domain lengths, flat-versus-bracketed choice)
/// against expected values.
/// </summary>
public sealed class VariadicSurfaceScenario
{
    /// <summary>A complete, parseable Mist declaration embedding the surface form.</summary>
    public string Source { get; }

    /// <summary>The surface-form category (e.g., <c>"params-pack"</c>, <c>"value-pack"</c>).</summary>
    public string Category { get; }

    /// <summary>The declared pack identifiers embedded in the scenario (empty when not applicable).</summary>
    public IReadOnlyList<string> PackNames { get; }

    /// <summary>The fixed-size domain length for a <c>Type[N]:</c> pack, or <c>null</c> otherwise.</summary>
    public int? FixedArity { get; }

    /// <summary>
    /// For a use-site argument-list scenario: <c>true</c> for the bracketed form
    /// (<c>Foo&lt;[int, string]&gt;</c>), <c>false</c> for the flat form
    /// (<c>Foo&lt;int, string&gt;</c>); <c>null</c> when not an argument-list scenario.
    /// </summary>
    public bool? IsBracketed { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
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
/// FsCheck generators for Mist variadic surface forms. Each generated scenario is a
/// complete, parseable Mist declaration exercising one surface form:
/// <list type="bullet">
///   <item>Pack type-parameter declarations in the <c>params TArgs</c>,
///     <c>Type[]:TArgs</c>, and <c>Type[N]:TArgs</c> spellings, including a middle pack</item>
///   <item>Value packs (<c>params TArgs args</c>) and value-level <c>params T[]</c></item>
///   <item>Plain <c>(TArgs...)</c> tuple parameters (no <c>params</c>)</item>
///   <item>Type-side spreads in field, type-argument-list, and base-type positions,
///     and value-side spreads (forward and zip)</item>
///   <item>Flat (<c>Foo&lt;int, string&gt;</c>) and bracketed (<c>Foo&lt;[int, string]&gt;</c>)
///     use-site argument lists, including the empty bracketed pack</item>
/// </list>
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

    /// <summary>
    /// Generates the <c>params</c> pack spelling: <c>public class Bundle&lt;params TArgs&gt; { }</c>.
    /// </summary>
    public static Gen<VariadicSurfaceScenario> GenParamsPackDecl()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"public class {typeName}<params {pack}> {{ }}"
               select new VariadicSurfaceScenario(
                   source, "params-pack", [pack], null, null,
                   $"params-pack: {source}");
    }

    /// <summary>
    /// Generates the explicit unknown-length pack spelling:
    /// <c>public class Box&lt;Type[]:TArgs&gt; { }</c>.
    /// </summary>
    public static Gen<VariadicSurfaceScenario> GenExplicitPackUnsizedDecl()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"public class {typeName}<Type[]:{pack}> {{ }}"
               select new VariadicSurfaceScenario(
                   source, "explicit-pack-unsized", [pack], null, null,
                   $"explicit-pack-unsized: {source}");
    }

    /// <summary>
    /// Generates the explicit fixed-size pack spelling with arity 1–4:
    /// <c>public struct ShapePair&lt;Type[2]:TShapes&gt; { }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a middle pack bound by the binding rule:
    /// <c>public class Between&lt;TFirst, params TMid, TLast&gt; { }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a value pack parameter on a method-level pack:
    /// <c>public void Take&lt;params TArgs&gt;(params TArgs args) { }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a homogeneous value-level params array parameter:
    /// <c>public void Log(params string[] parts) { }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a plain tuple parameter (no <c>params</c>):
    /// <c>public void Take&lt;params TArgs&gt;((TArgs...) rest) { }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a type-side spread in a member (field) position:
    /// <c>public class Bundle&lt;params TArgs&gt; { public TArgs... Item; }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a type-side spread in a type-argument list, via an alias that
    /// spreads the pack into <c>Func</c>:
    /// <c>type Action&lt;params TArgs&gt; = Func&lt;TArgs..., void&gt;;</c>.
    /// </summary>
    public static Gen<VariadicSurfaceScenario> GenTypeSpreadTypeArg()
    {
        return from typeName in Gen.Elements(TypeNames)
               from pack in Gen.Elements(PackNames)
               let source = $"type {typeName}<params {pack}> = Func<{pack}..., void>;"
               select new VariadicSurfaceScenario(
                   source, "type-spread-typearg", [pack], null, null,
                   $"type-spread-typearg: {source}");
    }

    /// <summary>
    /// Generates a type-side spread in a base-type list, in both the spread-into-
    /// type-arguments form (<c>: ICountable&lt;TArgs...&gt;</c>) and the bare base
    /// spread form (<c>: TArgs...</c>).
    /// </summary>
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

    /// <summary>
    /// Generates a value-side spread forwarding a value pack into a call:
    /// <c>public void Forward&lt;params TArgs&gt;(params TArgs args) { Consume(args...); }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a value-side zip spread populating spread-generated members from a
    /// value pack in a constructor:
    /// <c>public class Bundle&lt;params TArgs&gt; { public TArgs... Item; public Bundle(params TArgs values) { Item... = values...; } }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a flat use-site type-argument list as a field type:
    /// <c>public class C { public Box&lt;int, string&gt; value; }</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a bracketed use-site pack argument as a field type, including the
    /// empty bracketed pack:
    /// <c>public class C { public Box&lt;[int, string]&gt; value; }</c> or <c>Box&lt;[]&gt;</c>.
    /// </summary>
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

    /// <summary>
    /// Generates a random variadic surface-form scenario covering every category:
    /// pack declarations (all spellings and a middle pack), value packs, value-level
    /// <c>params</c>, plain tuple parameters, type- and value-side spreads, and
    /// flat/bracketed use-site argument lists.
    /// </summary>
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

    /// <summary>
    /// FsCheck Arbitrary for <see cref="VariadicSurfaceScenario"/>.
    /// Register this with
    /// <c>[Property(Arbitrary = new[] { typeof(MistVariadicsGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<VariadicSurfaceScenario> ArbitraryVariadicSurfaceScenario()
    {
        return GenVariadicSurface().ToArbitrary();
    }
}