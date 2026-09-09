using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Which bodies await, and which members each body calls, recorded as they are bound.
/// <para>
/// Recorded rather than searched for, for the reason the registries beside it are: half of these
/// awaits are INSERTED by the binder where a sync context calls a task-returning member, so they
/// exist in no syntax tree and only the binder ever sees them. Sitting beside the constraint
/// closures and the variadic uses, which exist for the same reason and are read by the same consumer.
/// </para>
/// <para>
/// A fact about the program, not about any target. What is DONE with it -- promoting a C# method to
/// async so an inserted await can be a real one rather than a blocking get -- belongs to the backend
/// and is no part of this. Another backend may block instead and would read the same facts.
/// </para>
/// </summary>
internal sealed class AwaitUseRegistry
{
    private readonly HashSet<Method> awaiting = [];
    private readonly Dictionary<Method, HashSet<Method>> callees = [];

    /// <summary>
    /// The bodies containing an await, whether the author wrote it or the binder supplied it.
    /// </summary>
    public IReadOnlySet<Method> Awaiting => awaiting;

    public void RecordAwait(Method? enclosing)
    {
        if(enclosing is not null)
        {
            _ = awaiting.Add(enclosing);
        }
    }

    public void RecordCall(Method? enclosing, Method? callee)
    {
        if(enclosing is null || callee is null || ReferenceEquals(enclosing, callee))
        {
            return;
        }

        if(!callees.TryGetValue(enclosing, out var set))
        {
            set = [];
            callees[enclosing] = set;
        }

        _ = set.Add(callee);
    }

    public IReadOnlySet<Method> CalleesOf(Method method)
    {
        return callees.TryGetValue(method, out var set) ? set : new HashSet<Method>();
    }

    public IEnumerable<Method> Callers => callees.Keys;
}