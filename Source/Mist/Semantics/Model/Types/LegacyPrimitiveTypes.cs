namespace Mist.Semantics.Model.Types;

/// <summary>
/// Lightweight <see cref="StructType"/> singletons that serve as fallback
/// type-identity markers when the Standard Library has not been loaded
/// (e.g., in unit tests that build a semantic model from a minimal AST
/// without stdlib source). Each instance carries the legacy keyword
/// display name (<c>"int"</c>, <c>"bool"</c>, etc.) and an empty member
/// scope.
///
/// <para>
/// These replace the deleted <c>PrimitiveType</c> singletons (Task 49).
/// Production code that loads the stdlib never reaches these — the
/// <see cref="SemanticModelBuilder"/> resolves primitive keywords to the
/// real <c>Standard.*</c> types via <c>ResolvePrimitiveKeyword</c>. The
/// fallback path in <c>ResolveLegacyPrimitive</c> and
/// <c>TryResolveWellKnownType</c> returns these singletons only when the
/// stdlib is absent.
/// </para>
/// </summary>
internal static class LegacyPrimitiveTypes
{
    internal static readonly StructType Int = new("int", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Uint = new("uint", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Long = new("long", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Ulong = new("ulong", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Short = new("short", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Ushort = new("ushort", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Byte = new("byte", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Sbyte = new("sbyte", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Float = new("float", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Double = new("double", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Decimal = new("decimal", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Bool = new("bool", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Char = new("char", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly ClassType String = new("string", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly ClassType Regex = new("regex", new Scope(null), containingNamespace: null) { IsReadonly = true };
    internal static readonly StructType Void = new("void", new Scope(null), containingNamespace: null);
    internal static readonly ClassType Type = new("Type", new Scope(null), containingNamespace: null);
}