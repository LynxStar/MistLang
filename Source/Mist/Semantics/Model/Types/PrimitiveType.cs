using System.Diagnostics.CodeAnalysis;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A well-known primitive type (int, bool, char, etc.). All primitives are Struct.
/// The semantic model knows only about Mist types — backend-specific mappings
/// (e.g., to C# keywords) live in the respective backend.
///
/// <para>
/// <b>Migration status (Task 32 of the Mist Standard Library spec):</b>
/// The static accessors (<see cref="Int"/>, <see cref="Bool"/>, etc.) have
/// been widened from <c>PrimitiveType</c> fields to <see cref="IType"/>
/// properties. After <see cref="SemanticModelBuilder"/> finishes Pass 1,
/// the corresponding Standard Library type (e.g.,
/// <c>Standard.Int32</c> for <see cref="Int"/>) is registered into
/// <see cref="PrimitiveTypeRegistry"/>, and these accessors then return
/// the stdlib <see cref="IType"/> instance (a
/// <see cref="StructType"/> or <see cref="ClassType"/>). Before
/// registration — or in test harnesses that build a semantic model without
/// loading stdlib sources — the accessors fall back to the private legacy
/// <c>PrimitiveType</c> singletons below so the rest of the pipeline
/// continues to function.
/// </para>
/// <para>
/// Reference equality (Requirement 8.5) is preserved across every access:
/// within a single process, once a primitive keyword has been registered
/// with its stdlib type, every subsequent read of the corresponding
/// accessor returns the same <see cref="IType"/> instance.
/// </para>
/// <para>
/// Stage 6 of the migration (Tasks 49–52) deletes this class entirely.
/// </para>
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Field names intentionally match Mist language primitive type names.")]
public sealed class PrimitiveType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Struct;

    // ── Legacy singletons ────────────────────────────────────────────────
    // Each private static field is the pre-migration canonical instance for
    // the corresponding primitive keyword. It serves as the fallback the
    // property getters return when PrimitiveTypeRegistry has no entry for
    // the keyword (e.g., during the bootstrap window of Pass 1, or in unit
    // tests that never load the stdlib). Exposed to the Mist assembly via
    // InternalsVisibleTo but otherwise opaque.
    internal static readonly PrimitiveType LegacyInt = new("int");
    internal static readonly PrimitiveType LegacyUint = new("uint");
    internal static readonly PrimitiveType LegacyLong = new("long");
    internal static readonly PrimitiveType LegacyUlong = new("ulong");
    internal static readonly PrimitiveType LegacyShort = new("short");
    internal static readonly PrimitiveType LegacyUshort = new("ushort");
    internal static readonly PrimitiveType LegacyByte = new("byte");
    internal static readonly PrimitiveType LegacySbyte = new("sbyte");
    internal static readonly PrimitiveType LegacyFloat = new("float");
    internal static readonly PrimitiveType LegacyDouble = new("double");
    internal static readonly PrimitiveType LegacyDecimal = new("decimal");
    internal static readonly PrimitiveType LegacyBool = new("bool");
    internal static readonly PrimitiveType LegacyChar = new("char");

    // ── Public accessors (compat shim) ───────────────────────────────────
    // Each property returns the stdlib IType registered for its
    // corresponding primitive keyword (e.g., "int" → Standard.Int32) when
    // PrimitiveTypeRegistry has an entry for it; otherwise it falls back
    // to the legacy singleton above. The return type is widened to IType
    // (Requirement 8.5) so stdlib StructType / ClassType instances can
    // flow through unchanged.
    public static IType Int => PrimitiveTypeRegistry.TryGet("int") ?? LegacyInt;
    public static IType Uint => PrimitiveTypeRegistry.TryGet("uint") ?? LegacyUint;
    public static IType Long => PrimitiveTypeRegistry.TryGet("long") ?? LegacyLong;
    public static IType Ulong => PrimitiveTypeRegistry.TryGet("ulong") ?? LegacyUlong;
    public static IType Short => PrimitiveTypeRegistry.TryGet("short") ?? LegacyShort;
    public static IType Ushort => PrimitiveTypeRegistry.TryGet("ushort") ?? LegacyUshort;
    public static IType Byte => PrimitiveTypeRegistry.TryGet("byte") ?? LegacyByte;
    public static IType Sbyte => PrimitiveTypeRegistry.TryGet("sbyte") ?? LegacySbyte;
    public static IType Float => PrimitiveTypeRegistry.TryGet("float") ?? LegacyFloat;
    public static IType Double => PrimitiveTypeRegistry.TryGet("double") ?? LegacyDouble;
    public static IType Decimal => PrimitiveTypeRegistry.TryGet("decimal") ?? LegacyDecimal;
    public static IType Bool => PrimitiveTypeRegistry.TryGet("bool") ?? LegacyBool;
    public static IType Char => PrimitiveTypeRegistry.TryGet("char") ?? LegacyChar;

    private PrimitiveType(string displayName) { DisplayName = displayName; }
}
