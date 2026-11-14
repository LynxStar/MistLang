using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Which bodies drop a task, and which members each body calls, recorded as they are bound.
/// <para>
/// Recorded rather than searched for because half of these drops are INSERTED by the binder where a
/// sync context calls a task-returning member, so they exist in no syntax tree.
/// </para>
/// <para>
/// A fact about the program, not about any target: a task's Drop is a yield point, whatever spells
/// it. Promoting a C# method to async so the drop is a real await belongs to the backend; another
/// backend may block instead.
/// </para>
/// </summary>
internal sealed class TaskDropRegistry
{
    private readonly HashSet<Method> dropping = [];
    private readonly Dictionary<Method, HashSet<Method>> callees = [];

    /// <summary>
    /// The bodies dropping a task, whether the author wrote the drop, wrote it as an await, or the
    /// binder supplied it.
    /// </summary>
    public IReadOnlySet<Method> Dropping => dropping;

    public void RecordDrop(Method? enclosing)
    {
        if(enclosing is not null)
        {
            _ = dropping.Add(enclosing);
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