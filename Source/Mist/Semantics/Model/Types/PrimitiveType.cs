using System.Diagnostics.CodeAnalysis;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A well-known primitive type (int, bool, char, etc.). All primitives are Struct.
/// The semantic model knows only about Mist types — backend-specific mappings
/// (e.g., to C# keywords) live in the respective backend.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Field names intentionally match Mist language primitive type names.")]
public sealed class PrimitiveType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Struct;

    public static readonly PrimitiveType Int = new("int");
    public static readonly PrimitiveType Uint = new("uint");
    public static readonly PrimitiveType Long = new("long");
    public static readonly PrimitiveType Ulong = new("ulong");
    public static readonly PrimitiveType Short = new("short");
    public static readonly PrimitiveType Ushort = new("ushort");
    public static readonly PrimitiveType Byte = new("byte");
    public static readonly PrimitiveType Sbyte = new("sbyte");
    public static readonly PrimitiveType Float = new("float");
    public static readonly PrimitiveType Double = new("double");
    public static readonly PrimitiveType Decimal = new("decimal");
    public static readonly PrimitiveType Bool = new("bool");
    public static readonly PrimitiveType Char = new("char");

    private PrimitiveType(string displayName) { DisplayName = displayName; }
}
