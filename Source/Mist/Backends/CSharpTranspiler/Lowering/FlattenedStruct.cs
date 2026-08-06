using System.Collections.Immutable;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A struct's members with its inherited ones folded in, which is how struct inheritance reaches
/// C#: the derived struct carries its ancestors' state and behaviour directly rather than
/// deriving, because a C# struct cannot inherit.
/// <para>
/// Every struct flattens its whole chain from the root independently, so a flattened struct never
/// reads a member off one of its ancestors. That is what keeps accessibility honest -- with no
/// cross-type access there is nothing to widen, and an ancestor's private field stays private on
/// the struct that absorbed it.
/// </para>
/// </summary>
/// <param name="Fields">
/// Every field the struct carries, its ancestors' first and each level in declaration order.
/// <para>
/// THIS ORDER DOES NOT MAKE REINTERPRETATION SAFE, and the sequential layout
/// <see cref="Emitter.TypeEmitter"/> requests does not either. The CLR honours that request only
/// for a struct holding no managed reference, transitively; with one anywhere inside it, references
/// are grouped first for GC tracking and the declared order is discarded. A derived struct adding a
/// reference of its own therefore displaces its ancestor's fields, so a shared prefix does NOT sit
/// at shared offsets. Generic structs are auto-laid-out regardless. Measured both ways.
/// </para>
/// <para>
/// Which is why <see cref="Emitter.StructConversionEmitter"/> copies rather than reinterprets. A
/// reinterpreting path would be sound only where every field in the whole chain is an unmanaged
/// value type and no type parameter is involved -- decidable here, but not yet needed, since the
/// only thing that requires aliasing is <c>ref</c> widening and this backend has no <c>ref</c> yet.
/// </para>
/// </param>
internal sealed record FlattenedStruct(
    ImmutableArray<FlattenedField> Fields,
    ImmutableArray<Method> Methods,
    ImmutableArray<Property> Properties)
{
    /// <summary>
    /// The ancestors this struct absorbed, root first.
    /// </summary>
    public ImmutableArray<AbsorbedAncestor> Ancestors { get; init; } = [];
}

/// <summary>
/// A field as it will be emitted on the flattened struct, which is not always how it was
/// declared: its type may have been substituted on the way down a generic chain, and its
/// accessibility is remapped by <see cref="StructFlattener"/>.
/// </summary>
/// <param name="Field">The declaration this came from, on whichever ancestor declared it.</param>
/// <param name="Type">
/// The field's type in terms of the flattened struct. Differs from <c>Field.Type</c> only when a
/// generic ancestor's type parameter was substituted -- <c>Container&lt;T&gt;.Value</c> becomes
/// <c>int</c> on a struct extending <c>Container&lt;int&gt;</c>.
/// </param>
/// <param name="Accessibility">
/// The accessibility to emit, which is <see cref="Accessibility.Private"/> wherever the
/// declaration said <c>protected</c>. C# rejects a protected struct member outright, and after
/// flattening nothing outside the struct reads the field, so private is both legal and the
/// narrowest thing that works.
/// </param>
internal sealed record FlattenedField(
    Field Field,
    IMistType Type,
    Accessibility Accessibility)
{
    public string Name => Field.Name;

    public required StructType DeclaringStruct { get; init; }
}

/// <summary>
/// An ancestor a struct absorbed.
/// </summary>
/// <param name="Reference">
/// How the ancestor was named at the point it was inherited, which is what a conversion operator
/// has to spell. Differs from <paramref name="Definition"/> for a generic ancestor: extending
/// <c>Container&lt;T&gt;</c> converts to <c>Container&lt;T&gt;</c>, not to the bare definition.
/// </param>
internal sealed record AbsorbedAncestor(StructType Definition, IMistType Reference);