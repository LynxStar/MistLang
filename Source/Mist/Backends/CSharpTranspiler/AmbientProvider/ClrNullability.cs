using System.Reflection;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Whether a CLR member's result can actually be null, read from the nullable metadata the C#
/// compiler writes.
/// <para>
/// WITHOUT THIS EVERY RESULT IS PESSIMISTIC. Mist has no null and a CLR reference can be one, so an
/// inbound result is closed over <c>| void</c> -- but most of the BCL says outright that it never
/// returns null, and wrapping those makes a caller narrow something that cannot be absent.
/// <c>xs.Skip(5).First()</c> is the shape it breaks: Skip is annotated non-null and First does not
/// resolve on an optional.
/// </para>
/// <para>
/// ABSENT METADATA STAYS PESSIMISTIC. An assembly compiled without nullable annotations is
/// OBLIVIOUS, not non-null, and the difference is the whole soundness argument -- reading silence
/// as a promise is how the naked null this rule exists to stop would get back in.
/// </para>
/// </summary>
internal static class ClrNullability
{
    private const string NullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttribute = "System.Runtime.CompilerServices.NullableContextAttribute";

    private const byte Oblivious = 0;
    private const byte Nullable = 2;

    /// <summary>
    /// Whether a member's result is declared NON-null, so nothing has to be unwrapped to use it.
    /// </summary>
    public static bool ResultIsNonNull(ICustomAttributeProvider result, MemberInfo member)
    {
        if(FirstFlag(CustomAttributes(result), NullableAttribute) is { } stated)
        {
            return stated is not (Nullable or Oblivious);
        }

        // The member's own context, then its type's, then the assembly's -- the same order the C#
        // compiler wrote them in, each standing for everything inside it that said nothing.
        foreach(var scope in Scopes(member))
        {
            if(FirstFlag(scope, NullableContextAttribute) is { } context)
            {
                return context is not (Nullable or Oblivious);
            }
        }

        return false;
    }

    private static IEnumerable<IEnumerable<CustomAttributeData>> Scopes(MemberInfo member)
    {
        yield return CustomAttributes(member);

        if(member.DeclaringType is { } declaring)
        {
            yield return CustomAttributes(declaring);
            yield return SafeAttributes(declaring.Module.GetCustomAttributesData);
        }
    }

    private static IEnumerable<CustomAttributeData> CustomAttributes(object target)
    {
        return SafeAttributes(() => target switch
        {
            MemberInfo member => member.GetCustomAttributesData(),
            ParameterInfo parameter => parameter.GetCustomAttributesData(),
            _ => [],
        });
    }

    private static IEnumerable<CustomAttributeData> SafeAttributes(Func<IList<CustomAttributeData>> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// The flag for the OUTERMOST type. A generic result carries one byte per type it mentions, in
    /// the order they are written, and only the first is about the result itself -- the rest belong
    /// to its arguments, whose own nullability this rule does not reach.
    /// </summary>
    private static byte? FirstFlag(IEnumerable<CustomAttributeData> attributes, string attributeName)
    {
        foreach(var attribute in attributes)
        {
            if(attribute.AttributeType.FullName != attributeName
                || attribute.ConstructorArguments.Count != 1)
            {
                continue;
            }

            var argument = attribute.ConstructorArguments[0];

            if(argument.Value is byte flag)
            {
                return flag;
            }

            if(argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> { Count: > 0 } flags)
            {
                return flags.First().Value as byte?;
            }
        }

        return null;
    }
}