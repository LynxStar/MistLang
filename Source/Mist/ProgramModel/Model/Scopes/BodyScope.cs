using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using SourceSpan = Storm.Runtime.SourceSpan;
using SyntaxNode = Storm.Runtime.SyntaxNode;

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

    private readonly Dictionary<string, Method> functions = [];

    private readonly Dictionary<LocalFunctionDeclarationNode, Method> functionsByNode = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<LocalVariable, IMistType?> narrowed = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyDictionary<string, LocalVariable> Locals => locals;

    /// <summary>
    /// The local function built for each declaration in this scope's statement list, kept by node
    /// rather than by name so a declaration whose name was refused as a duplicate still binds its
    /// own body rather than the first declaration's.
    /// </summary>
    public IReadOnlyDictionary<LocalFunctionDeclarationNode, Method> LocalFunctions => functionsByNode;

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
        if(locals.TryGetValue(request.Name, out var local))
        {
            return new VariableResult(local);
        }

        return functions.TryGetValue(request.Name, out var function)
            ? new MethodGroupResult([function])
            : new NotFoundResult();
    }

    /// <summary>
    /// Declares a local or parameter. Errors only if THIS scope already declares the name -- an
    /// outer scope declaring it is shadowing, not a conflict.
    /// </summary>
    public void DeclareLocal(LocalVariable local, SyntaxNode declaration, DiagnosticBag diagnostics, SourceSpan? nameSpan = null)
    {
        if(local.IsDiscard)
        {
            return;
        }

        if(functions.ContainsKey(local.Name) || !locals.TryAdd(local.Name, local))
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.DuplicateLocalVariable, declaration, nameSpan ?? declaration.Span, null, local.Name);
        }
    }

    public void DeclareLocalFunction(LocalFunctionDeclarationNode node, Method function, DiagnosticBag diagnostics)
    {
        functionsByNode[node] = function;

        if(locals.ContainsKey(function.Name) || !functions.TryAdd(function.Name, function))
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.DuplicateLocalFunction, node, null, function.Name);
        }
    }
}