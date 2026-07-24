using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Entry point for Phase 3 body binding. Sets up the scope (parameter aliases)
/// and delegates to <see cref="StatementBinder"/> for the actual statement resolution.
/// </summary>
internal static class BodyBinder
{
    /// <summary>
    /// Binds a delegate (method, constructor, operator, cast operator): seeds the
    /// scope with parameter aliases, then binds the body.
    /// </summary>
    public static void BindDelegate(Method method, MemberType enclosingType, BuilderContext context)
    {
        var bodyScope = new Scope(method.Scope);
        foreach(var parameter in method.ParametersList)
        {
            bodyScope.RegisterLocal(new LocalVariable(parameter.Name, parameter.Type), context.Diagnostics);
        }

        BindBody(method.Body!, bodyScope, method.ReturnType, enclosingType, context);
    }

    /// <summary>
    /// Binds a body block: resolves statements in the given scope. Used directly by
    /// property/indexer accessors (whose parameters are already in scope) and
    /// indirectly by <see cref="BindDelegate"/>.
    /// </summary>
    public static void BindBody(Block body, Scope scope, IMistType? returnType, MemberType? enclosingType, BuilderContext context)
    {
        var bindingCtx = new BindingContext(scope, returnType) { EnclosingType = enclosingType };
        var statements = new List<Statement>(body.Node.Statements.Length);
        foreach(var node in body.Node.Statements)
        {
            statements.Add(StatementBinder.BindStatement(node, bindingCtx, context));
        }

        body.Statements = statements;
    }
}