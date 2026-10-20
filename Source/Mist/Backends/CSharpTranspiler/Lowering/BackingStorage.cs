using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The C# member that holds a property's storage. An auto-property keeps C#'s synthesized field,
/// which assigning the property writes and nothing else runs, so a default and a constructor fill
/// the slot by assigning the property. A property with an accessor of its own declares a private
/// field instead (properties Requirement 14.6): assigning such a property would run its setter, and
/// a default runs no user code (defaultability Requirement 6), so the slot is written by name.
/// </summary>
internal static class BackingStorage
{
    public static string? DeclaredField(Property property)
    {
        return property is { Modifiers.IsIntrinsic: false, HasBackingStorage: true }
            && !(AccessorEmitter.IsBodiless(property.Getter) && AccessorEmitter.IsBodiless(property.Setter))
            ? $"__{property.Name}_field"
            : null;
    }

    public static string Slot(InstanceStorage storage)
    {
        return storage.Property is { } property && DeclaredField(property) is { } field ? field : IdentifierSpelling.Of(storage.Name);
    }
}