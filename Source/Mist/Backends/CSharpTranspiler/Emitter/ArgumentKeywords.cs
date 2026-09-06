using Mist.ProgramModel.Contracts.Types;
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
/// whose alphabet is "" / ref / ref readonly; this spells a USE, whose alphabet is "" / ref / in /
/// out. Sharing a signature would put the CLR calling convention and the ownership-lending
/// modifiers in one function that has to mean different things to each caller.
/// </para>
/// </summary>
internal static class ArgumentKeywords
{
    /// <summary>
    /// One arm per direction, with no inference left to do here. Deciding what a parameter IS
    /// belongs to whatever built it -- the projector reading CLR metadata, or the builder reading
    /// Mist source -- because only they can see the evidence. A genuine <c>in</c> and an
    /// <c>[In] ref</c> reach this function as the same modifier flags and need opposite keywords, so
    /// any rule written here from the flags alone is wrong for one of them whichever way it goes.
    /// That is what an earlier version of this file did, and it was right only because a
    /// misclassification upstream happened to cancel it out.
    /// </summary>
    public static string For(Parameter parameter)
    {
        return parameter.Modifier.Direction switch
        {
            ByRefDirection.Ref => "ref ",
            ByRefDirection.In => "in ",
            ByRefDirection.Out => "out ",
            _ => "",
        };
    }
}