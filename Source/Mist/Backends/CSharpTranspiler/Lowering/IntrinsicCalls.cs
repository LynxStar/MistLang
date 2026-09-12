using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The intrinsic members whose C# is not the call the emitter would otherwise write. An `intrinsic`
/// declaration is the stdlib's promise that the backend supplies the member, and most are kept with no
/// entry here: an operator emits as an operator, and a member of a type the backing map projects emits
/// as a call on that type, which is why Console.WriteLine and Math.Abs need nothing. Listed is what
/// Mist spells differently from the CLR. Type.Of is `GetType` on the value. Clone on a backend-backed
/// type IS the value, a struct copying on assignment and String being immutable. Drop on a task binds
/// as the await it is (PostfixBinder), so it reaches emission as a call only through a conditional
/// access, where it is the join the drop-semantics spec says Drop is (Req 7.2), spelled as the emitter
/// spells an await that cannot be real. An array's GetEnumerator goes through the array's enumerable
/// view, see <see cref="ArrayEnumeration"/>. A regex's full match and Pattern are derived from members
/// the CLR has. The task combinators and array generation are runtime helpers.
/// <para>
/// <see cref="IntrinsicSupply"/> reads <see cref="Classify"/> to tell a kept promise from an unkept
/// one, which is what makes this list the whole of the respellings: a member absent from here and
/// from CLR metadata is refused rather than emitted.
/// </para>
/// </summary>
internal static class IntrinsicCalls
{
    private enum Respelling
    {
        None,

        TypeOf,

        CloneAsSelf,

        TaskDrop,

        TaskCombinator,

        RegexFullMatch,

        ArrayGeneration,

        ArrayEnumerator,
    }

    public static bool Respells(Method? callee)
    {
        return Classify(callee) != Respelling.None;
    }

    public static bool RespellsProperty(string name, IMistType? type)
    {
        return name == "Pattern" && RegexLowering.Is(type);
    }

    public static string RewriteProperty(string receiver)
    {
        return RegexLowering.PatternOf(receiver);
    }

    public static string Rewrite(
        Method callee, string? receiver, IMistType? receiverType, IReadOnlyList<string> arguments)
    {
        return Classify(callee) switch
        {
            Respelling.TypeOf => $"{arguments[0]}.GetType()",
            Respelling.CloneAsSelf => Receiver(callee, receiver),
            Respelling.TaskDrop => $"{Receiver(callee, receiver)}.GetAwaiter().GetResult()",
            Respelling.ArrayEnumerator => ArrayEnumeration.Rewrite(
                Receiver(callee, receiver),
                receiverType ?? throw new InvalidOperationException($"ICE: '{callee.Name}' was invoked on an untyped receiver.")),
            Respelling.TaskCombinator => TaskCombinators.Rewrite(callee, arguments),
            Respelling.RegexFullMatch => RegexLowering.IsFullMatch(
                arguments[0], RegexLowering.PatternOf(Receiver(callee, receiver))),
            _ => throw new InvalidOperationException(
                $"ICE: '{callee.Name}' is not an invocation the backend respells."),
        };
    }

    public static bool ClonesAsItself(IMistType? type)
    {
        return type is not null && SlotLift.Definition(type) is ConcreteType { IsIntrinsic: true };
    }

    private static string Receiver(Method callee, string? receiver)
    {
        return receiver
            ?? throw new InvalidOperationException($"ICE: '{callee.Name}' was invoked without a receiver.");
    }

    private static Respelling Classify(Method? callee)
    {
        if(callee is not { Modifiers.IsIntrinsic: true })
        {
            return Respelling.None;
        }

        var declaring = callee.DeclaringType;

        return callee switch
        {
            Constructor constructor when ArrayLowering.Generates(constructor) => Respelling.ArrayGeneration,
            _ when ArrayEnumeration.Enumerates(callee) => Respelling.ArrayEnumerator,
            { Name: "Of", ParametersList.Count: 1 }
                when declaring?.FullyQualifiedName == WellKnownTypes.Type.FullyQualifiedName => Respelling.TypeOf,
            { Name: "Clone", ParametersList.Count: 0 } when ClonesAsItself(declaring) => Respelling.CloneAsSelf,
            { Name: "Drop", ParametersList.Count: 0 } when IsTaskFamily(declaring) => Respelling.TaskDrop,
            { Name: "IsFullMatch", ParametersList.Count: 1 } when RegexLowering.Is(declaring) => Respelling.RegexFullMatch,
            _ when TaskCombinators.Combines(callee) => Respelling.TaskCombinator,
            _ => Respelling.None,
        };
    }

    private static bool IsTaskFamily(MemberType? declaring)
    {
        return declaring is not null
            && (declaring.FullyQualifiedName == WellKnownTypes.TaskGeneric.FullyQualifiedName
                || declaring.FullyQualifiedName == WellKnownTypes.ValueTaskGeneric.FullyQualifiedName);
    }
}