using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The by-ref keyword an ARGUMENT takes, read from the parameter it fills.
/// <para>
/// Mist has one by-ref spelling and C# has three, so the call site cannot carry the answer: the
/// grammar offers only <c>ref</c> (Mist.storm's RefArgument) and binding drops even that. The callee
/// is the only surviving record, which is also the one C# insists both sides agree on.
/// </para>
/// <para>
/// A SIBLING of <see cref="RefKeywords"/> rather than part of it. That one spells a DECLARATION,
/// whose alphabet is "" / ref / ref readonly and whose input is two bools; this one spells a USE,
/// whose alphabet is "" / ref / out and whose input is a whole parameter. Sharing a signature would
/// put the CLR calling convention and the ownership-lending modifiers in one function that has to
/// mean different things to each caller.
/// </para>
/// </summary>
internal static class ArgumentKeywords
{
    private const string OutAttributeName = "System.Runtime.InteropServices.OutAttribute";

    /// <summary>
    /// Both guards on the <c>out</c> arm are load-bearing and were measured against the .NET 10
    /// reference pack rather than assumed.
    /// <para>
    /// NOT-READONLY excludes <c>[In, Out] ref</c>, which is mutable <c>ref</c> in C# and sets the CLR
    /// Out flag anyway -- COM interop declares 43 such parameters in the WindowsDesktop pack. Emitting
    /// <c>out</c> there is CS1620 against a signature that wants <c>ref</c>.
    /// </para>
    /// <para>
    /// BY-REF excludes a by-VALUE parameter carrying <c>[Out]</c>, a marshalling annotation that says
    /// nothing about the calling convention; that pack declares nine. Emitting <c>out</c> there is
    /// CS1615.
    /// </para>
    /// <para>
    /// The readonly by-ref directions are deliberately left spelling <c>ref</c>. A genuine CLR
    /// <c>in</c> and an <c>[In] ref</c> project to the SAME model shape today, because
    /// AttributeProjector filters both InAttribute and IsReadOnlyAttribute, so nothing here can tell
    /// them apart -- and they need opposite keywords. Closing that needs a projection change and is
    /// its own capability.
    /// </para>
    /// </summary>
    public static string For(Parameter parameter)
    {
        if(!parameter.Modifier.IsRef)
        {
            return "";
        }

        return !parameter.Modifier.IsReadonly && IsOut(parameter) ? "out " : "ref ";
    }

    private static bool IsOut(Parameter parameter)
    {
        foreach(var attribute in parameter.AttributesList)
        {
            if(attribute.AttributeType.FullyQualifiedName == OutAttributeName)
            {
                return true;
            }
        }

        return false;
    }
}