using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// Locals and parameters: a member body, or any nested block within one. To name resolution those
/// are the same thing, so one kind serves both, nested by block.
/// <para>
/// Deliberately not where staticness lives. A body can be many blocks deep and the request must
/// narrow exactly once on the way out, which is the <see cref="MethodScope"/> above it.
/// </para>
/// </summary>
internal sealed class BodyScope(Scope parent) : Scope(parent)
{
    private readonly Dictionary<string, LocalVariable> locals = [];

    private readonly Dictionary<LocalVariable, IMistType?> narrowed = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyDictionary<string, LocalVariable> Locals => locals;

    /// <summary>
    /// What a `??=` established about a local for the statements after it in this scope and its
    /// nested scopes: the non-void type it now holds, or null once a later assignment could have
    /// made it void again. Lexical rather than flow-sensitive, as the optional-types spec words it.
    /// </summary>
    public void Narrow(LocalVariable local, IMistType? type)
    {
        narrowed[local] = type;
    }

    public static IMistType? NarrowedIn(Scope scope, LocalVariable local)
    {
        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is BodyScope body && body.narrowed.TryGetValue(local, out var type))
            {
                return type;
            }
        }

        return null;
    }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        // A local is neither instance nor static state, so member visibility does not apply: one
        // declared in a static member's body is reachable from it.
        return locals.TryGetValue(request.Name, out var local)
            ? new VariableResult(local)
            : new NotFoundResult();
    }

    /// <summary>
    /// Declares a local or parameter. Errors only if THIS scope already declares the name -- an
    /// outer scope declaring it is shadowing, not a conflict.
    /// </summary>
    public void DeclareLocal(LocalVariable local, DiagnosticBag diagnostics)
    {
        if(!locals.TryAdd(local.Name, local))
        {
            diagnostics.Add(new Diagnostic(
                ProgramModelDiagnostics.DuplicateLocalVariable.Id,
                ProgramModelDiagnostics.DuplicateLocalVariable.DefaultSeverity,
                ProgramModelDiagnostics.DuplicateLocalVariable.Stage,
                string.Format(ProgramModelDiagnostics.DuplicateLocalVariable.MessageTemplate, local.Name),
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));
        }
    }
}