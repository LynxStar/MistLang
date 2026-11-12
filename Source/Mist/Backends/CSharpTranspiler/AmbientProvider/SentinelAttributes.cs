using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Sentinels carrying CLR metadata that has no custom attribute of its own. They live in the C#
/// backend rather than the ProgramModel because they are target-specific, and the emitter recognizes
/// them by <see cref="IMistType.FullyQualifiedName"/> when reconstructing CLR modifiers.
/// </summary>
internal static class SentinelAttributes
{
    private const string Namespace = "Mist.Sentinel";

    public static readonly IMistType Sealed = new SentinelType("Sealed");

    public static readonly IMistType Abstract = new SentinelType("Abstract");

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
    /// Not registered in any namespace or type dictionary.
    /// </summary>
    private sealed class SentinelType : MistType, IMistType
    {
        public SentinelType(string name)
            : base(name, $"{Namespace}.{name}", new Scope(null), declaringType: null)
        {
        }
    }
}