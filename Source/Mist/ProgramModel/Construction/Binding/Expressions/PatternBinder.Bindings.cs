using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static partial class PatternBinder
{
    /// <summary>
    /// Declares a pattern's binding, or answers null where the pattern keeps nothing. The grammar puts
    /// the discard in the SAME slot as a name (<c>name:(IDENTIFIER | DISCARD)</c>), so <c>int _</c>
    /// arrives as a type pattern carrying a discard token, never as a discard pattern, and the token's
    /// kind is the only thing separating the two readings.
    /// </summary>
    private static LocalVariable? Declare(
        SyntaxNode node,
        Token name,
        IMistType type,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context, bool scrutineeFailed)
    {
        return name.Kind == (int)TokenKind.DISCARD
            ? null
            : DeclareNamed(node, name.Lexeme, type, bindingsPermitted, ctx, context, scrutineeFailed, name.Span)
            ;
    }

    private static LocalVariable? DeclareNamed(
        SyntaxNode node,
        string name,
        IMistType type,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context, bool scrutineeFailed, SourceSpan? nameSpan = null)
    {
        if(!bindingsPermitted)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.PatternBindingNotEstablished, node, null, name);

            return null;
        }

        var local = new LocalVariable(name, type) { IsFailed = scrutineeFailed };
        ctx.Scope.DeclareLocal(local, node, context.Diagnostics, nameSpan);

        return local;
    }
}