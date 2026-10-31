using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Synthetic attribute types used as sentinels to carry CLR metadata that has no
/// corresponding CLR custom attribute. These live in the C# backend (not the
/// ProgramModel) because they represent target-specific concepts.
///
/// The emitter recognizes these by <see cref="IMistType.FullyQualifiedName"/> when
/// reconstructing CLR modifiers during code generation.
/// </summary>
internal static class SentinelAttributes
{
    private const string Namespace = "Mist.Sentinel";

    /// <summary>Marks a class as sealed (cannot be inherited).</summary>
    public static readonly IMistType Sealed = new SentinelType("Sealed");

    /// <summary>Marks a class as abstract (cannot be instantiated directly).</summary>
    public static readonly IMistType Abstract = new SentinelType("Abstract");

    /// <summary>
    /// Applies sentinel attributes to a type's attribute list based on CLR flags.
    /// Called during shell creation for classes (not interfaces or value types).
    /// </summary>
    public static void ApplyTypeAttributes(Type clrType, List<AttributeData> target)
    {
        var isStaticClass = clrType.IsAbstract && clrType.IsSealed;

        // Static classes are abstract+sealed in CLR — don't emit either sentinel.
        if(isStaticClass || clrType.IsInterface)
        {
            return;
        }

        if(clrType.IsSealed)
        {
            target.Add(new AttributeData(Sealed, [], []));
        }

        if(clrType.IsAbstract)
        {
            target.Add(new AttributeData(Abstract, [], []));
        }
    }

    /// <summary>
    /// A lightweight type used solely as the <see cref="AttributeData.AttributeType"/>
    /// for sentinel attributes. Not registered in any namespace or type dictionary.
    /// </summary>
    private sealed class SentinelType : MistType, IMistType
    {
        public SentinelType(string name)
            : base(name, $"{Namespace}.{name}", new Scope(null), declaringType: null)
        {
        }
    }
}