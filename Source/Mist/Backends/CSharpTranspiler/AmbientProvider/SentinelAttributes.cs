using System.Reflection;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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

    /// <summary>Marks a method as virtual (can be overridden).</summary>
    public static readonly IMistType Virtual = new SentinelType("Virtual");

    /// <summary>Marks a method as an override of a base method.</summary>
    public static readonly IMistType Override = new SentinelType("Override");

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
    /// Applies sentinel attributes to a method's attribute list based on CLR flags.
    /// Detects virtual, abstract, override, and sealed override.
    /// </summary>
    public static void ApplyMethodAttributes(MethodInfo method, List<AttributeData> target)
    {
        if(!method.IsVirtual)
        {
            return;
        }

        var isOverride = (method.Attributes & MethodAttributes.NewSlot) == 0;

        if(method.IsAbstract)
        {
            target.Add(new AttributeData(Abstract, [], []));
        }
        else if(isOverride)
        {
            target.Add(new AttributeData(Override, [], []));

            // A sealed override (final + virtual + not newslot) prevents further overriding.
            if(method.IsFinal)
            {
                target.Add(new AttributeData(Sealed, [], []));
            }
        }
        else
        {
            // New virtual slot (not override, not abstract).
            target.Add(new AttributeData(Virtual, [], []));
        }
    }

    /// <summary>
    /// A lightweight type used solely as the <see cref="AttributeData.AttributeType"/>
    /// for sentinel attributes. Not registered in any namespace or type dictionary.
    /// </summary>
    private sealed class SentinelType : MistType, IMistType
    {
        public SentinelType(string name)
            : base(name, Namespace, new Scope(null), declaringType: null)
        {
        }
    }
}