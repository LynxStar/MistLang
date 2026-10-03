using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Resolution;

internal readonly record struct TypeResolution(IMistType Type, bool IsFailed = false);

internal sealed partial class ScopeResolver
{
    private ResolutionFailure? resolutionFailure;

    public TypeResolution ResolveWithFailure(TypeReferenceNode node, Scope scope)
    {
        return ResolveWithFailure(() => Resolve(node, scope));
    }

    public TypeResolution ResolveWithFailure(SingleTypeRefNode node, Scope scope)
    {
        return ResolveWithFailure(() => Resolve(node, scope));
    }

    public TypeResolution ResolveNamedTypeWithFailure(string name, Scope scope, SyntaxNode node)
    {
        return ResolveWithFailure(() => ResolveNamedType(name, scope, node));
    }

    private TypeResolution ResolveWithFailure(Func<IMistType> resolve)
    {
        var previous = resolutionFailure;
        var current = new ResolutionFailure();
        resolutionFailure = current;

        try
        {
            return new TypeResolution(resolve(), current.IsFailed);
        }
        finally
        {
            resolutionFailure = previous;

            if(current.IsFailed)
            {
                MarkResolutionFailure();
            }
        }
    }

    private void MarkResolutionFailure()
    {
        if(resolutionFailure is { } failure)
        {
            failure.IsFailed = true;
        }
    }

    private void ReportResolutionFailure(
        DiagnosticDescriptor descriptor, SyntaxNode node, string? analyzerName, params ReadOnlySpan<object?> arguments)
    {
        context.Diagnostics.ReportAtNode(descriptor, node, analyzerName, arguments);
        MarkResolutionFailure();
    }

    private sealed class ResolutionFailure
    {
        public bool IsFailed { get; set; }
    }
}