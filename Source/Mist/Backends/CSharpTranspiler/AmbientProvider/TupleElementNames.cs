using System.Reflection;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal sealed class TupleElementNames
{
    private const string AttributeName = "System.Runtime.CompilerServices.TupleElementNamesAttribute";

    private readonly string?[] names;
    private int next;

    private TupleElementNames(string?[] names)
    {
        this.names = names;
    }

    /// <summary>
    /// Null unless the attribute holds exactly as many names as <see cref="ClrValueTuple.NameSlots"/>
    /// counts for the type. Names are placed by position alone, so a count that disagrees would hand
    /// every name after the disagreement to the wrong element; dropping them all leaves the tuples
    /// structural, which is still correct.
    /// </summary>
    public static TupleElementNames? Of(ICustomAttributeProvider owner, Type type)
    {
        return ClrValueTuple.NameSlots(type) == 0 ? null : OfNames(Read(owner), type);
    }

    internal static TupleElementNames? OfNames(string?[]? names, Type type)
    {
        return names is not null && names.Length == ClrValueTuple.NameSlots(type)
            ? new TupleElementNames(names)
            : null;
    }

    public string?[] Take(int count)
    {
        var taken = names[next..(next + count)];
        next += count;

        return taken;
    }

    public void Skip(int count)
    {
        next += count;
    }

    private static string?[]? Read(ICustomAttributeProvider owner)
    {
        foreach(var attribute in ClrNullability.CustomAttributes(owner))
        {
            if(attribute.AttributeType.FullName == AttributeName
                && attribute.ConstructorArguments is [{ Value: IReadOnlyCollection<CustomAttributeTypedArgument> values }])
            {
                return values.Select(value => value.Value as string).ToArray();
            }
        }

        return null;
    }
}