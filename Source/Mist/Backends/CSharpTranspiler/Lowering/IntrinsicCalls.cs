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
/// type IS the value, a struct copying on assignment and String being immutable. Drop on a task is
/// not listed: it is the one call spelled as a statement form rather than a call, `await`, which
/// <c>BodyEmitter.EmitTaskDrop</c> owns. An array's GetEnumerator constructs the struct the backend
/// generates, see <see cref="ArrayEnumeration"/>. A regex's full match and Pattern are derived from
/// members the CLR has. The task combinators and array generation are runtime helpers. String's case
/// mapping, Convert's text conversions and List's Sort name the invariant culture, see
/// <see cref="InvariantCulture"/>.
/// <para>
/// <see cref="IntrinsicSupply"/> reads <see cref="Respells"/> and <see cref="RespellsProperty"/> to
/// tell a kept promise from an unkept one, which is what makes this list the whole of the
/// respellings: a member absent from here and from CLR metadata is refused rather than emitted.
/// </para>
/// </summary>
internal static class IntrinsicCalls
{
    private enum Respelling
    {
        None,

        TypeOf,

        CloneAsSelf,

        TaskCombinator,

        RegexFullMatch,

        ArrayGeneration,

        ArrayEnumerator,

        InvariantCase,

        InvariantConversion,

        InvariantOrder,
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
        Method callee, string? receiver, IMistType? receiverType, IReadOnlyList<string> arguments, bool conditional = false)
    {
        var access = conditional ? "?." : ".";

        return Classify(callee) switch
        {
            Respelling.TypeOf => $"{arguments[0]}.GetType()",
            Respelling.CloneAsSelf => Receiver(callee, receiver),
            Respelling.ArrayEnumerator => ArrayEnumeration.Rewrite(
                Receiver(callee, receiver),
                receiverType ?? throw new InvalidOperationException($"ICE: '{callee.Name}' was invoked on an untyped receiver.")),
            Respelling.TaskCombinator => TaskCombinators.Rewrite(callee, arguments),
            Respelling.RegexFullMatch => RegexLowering.IsFullMatch(
                arguments[0], RegexLowering.PatternOf(Receiver(callee, receiver))),
            Respelling.InvariantCase => $"{Receiver(callee, receiver)}{access}{callee.Name}Invariant()",
            Respelling.InvariantConversion => $"System.Convert.{callee.Name}({string.Join(", ", arguments)}, {InvariantCulture.Provider})",
            Respelling.InvariantOrder => $"{Receiver(callee, receiver)}{access}Sort({InvariantCulture.Order(receiverType)})",
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
            { Name: "IsFullMatch", ParametersList.Count: 1 } when RegexLowering.Is(declaring) => Respelling.RegexFullMatch,
            { Name: "ToUpper" or "ToLower", ParametersList.Count: 0 } when Declares(declaring, WellKnownTypes.String.FullyQualifiedName)
                => Respelling.InvariantCase,
            { Modifiers.IsStatic: true } when Declares(declaring, "Standard.Convert") && ConvertsText(callee) => Respelling.InvariantConversion,
            { Name: "Sort", ParametersList.Count: 0 } when Declares(declaring, "Standard.List<>") => Respelling.InvariantOrder,
            _ when TaskCombinators.Combines(callee) => Respelling.TaskCombinator,
            _ => Respelling.None,
        };
    }

    private static bool Declares(MemberType? declaring, string fullyQualifiedName)
    {
        return declaring?.FullyQualifiedName == fullyQualifiedName;
    }

    private static bool ConvertsText(Method callee)
    {
        return callee.ParametersList.Any(parameter => parameter.Type.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName)
            || callee.ReturnType.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName;
    }
}