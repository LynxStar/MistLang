using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which Mist-SYNC methods must be emitted as C# async ones. A sync context awaits the tasks it calls,
/// and C# has only two ways to wait: an <c>await</c>, which requires the enclosing method be async, or
/// a blocking get, which occupies a thread and deadlocks under any synchronization context. So the
/// obligation travels: a method containing an await becomes async, its callers must await IT, and they
/// become async in turn.
/// <para>
/// A TARGET CONCERN, NOT A LANGUAGE ONE. Mist's method is sync and its callers see a sync method; this
/// is only how C# is made to honour that. A backend with blocking primitives would read the same
/// registry and emit none of it.
/// </para>
/// <para>
/// Ambient for the duration of an emission, for the reason <see cref="Monomorph"/> gives: the
/// alternative is a parameter threaded through every emitter that writes a signature or a call.
/// </para>
/// </summary>
internal sealed class AsyncPromotion
{
    [ThreadStatic]
    private static AsyncPromotion? inScope;

    private readonly IReadOnlySet<Method> promoted;
    private readonly ProgramModelClass programModel;

    private AsyncPromotion(IReadOnlySet<Method> promoted, ProgramModelClass programModel)
    {
        this.promoted = promoted;
        this.programModel = programModel;
    }

    /// <summary>
    /// A promoted method's emitted return type. Interned through the same definition the frontend's
    /// own async transform uses, so a promoted `void` and a declared `async void` reach emission as
    /// one type rather than two that only spell alike.
    /// </summary>
    public static IMistType WrapReturn(IMistType result)
    {
        if(inScope is not { } scope
            || scope.programModel.TypesByFQN.GetValueOrDefault(
                WellKnownTypes.TaskGeneric.FullyQualifiedName) is not MistType definition)
        {
            return result;
        }

        return scope.programModel.TypeInterner.GetOrCreateConstructedType(definition, new[] { result });
    }

    /// <summary>
    /// Whether a method Mist declared SYNC is emitted async, which is also exactly when a call to it
    /// has to be awaited: its emitted return type is a task the model never mentions.
    /// </summary>
    public static bool IsPromoted(Method? method)
    {
        return method is not null && inScope is { } scope && scope.promoted.Contains(method);
    }

    /// <summary>
    /// Whether the emitted method carries the <c>async</c> modifier -- because Mist said so, or
    /// because the obligation reached it.
    /// </summary>
    public static bool EmitsAsync(Method method)
    {
        return method.Modifiers.IsAsync || IsPromoted(method);
    }

    public static IDisposable Enter(ProgramModelClass programModel)
    {
        inScope = new AsyncPromotion(Compute(programModel.AwaitUses), programModel);
        return new Scope();
    }

    /// <summary>
    /// The awaiting bodies, then everything that reaches one. Seeded from bodies rather than from
    /// declarations because an inserted await is what makes a sync method need this, and a method
    /// Mist declared async is excluded throughout -- it already carries the modifier and its callers
    /// already await it, so promoting it again would wrap its return type twice.
    /// </summary>
    private static IReadOnlySet<Method> Compute(AwaitUseRegistry uses)
    {
        var promoted = new HashSet<Method>(uses.Awaiting.Where(method => !method.Modifiers.IsAsync));
        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var caller in uses.Callers)
            {
                if(caller.Modifiers.IsAsync || promoted.Contains(caller))
                {
                    continue;
                }

                if(uses.CalleesOf(caller).Any(promoted.Contains))
                {
                    _ = promoted.Add(caller);
                    changed = true;
                }
            }
        }

        return promoted;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            inScope = null;
        }
    }
}