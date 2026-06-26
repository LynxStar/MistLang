using Mist.Common.Text;

namespace Mist.Common.Diagnostics;

/// <summary>
/// Resolves source locations from AST node origins for diagnostic creation.
/// </summary>
public static class DiagnosticHelper
{
    private const int MaxChainDepth = 100;

    /// <summary>
    /// Resolves the source file path and line/column position from a node's origin.
    /// Chains through SyntheticOrigin references until a ParsedOrigin is found.
    /// </summary>
    public static (string Path, LinePositionSpan Location, string? SyntheticContext)
        ResolveLocation(Storm.Runtime.SyntaxNode node)
    {
        var origin = node.Origin;

        if(origin is null or TestOrigin)
        {
            return ("<unknown>", default, null);
        }

        if(origin is ParsedOrigin parsed)
        {
            var location = ComputeLocation(parsed.Source, node.Span);
            return (parsed.Source.Path, location, null);
        }

        if(origin is SyntheticOrigin synthetic)
        {
            return ResolveSyntheticChain(synthetic, node.Span);
        }

        return ("<unknown>", default, null);
    }

    private static (string Path, LinePositionSpan Location, string? SyntheticContext)
        ResolveSyntheticChain(SyntheticOrigin synthetic, Storm.Runtime.SourceSpan span)
    {
        var reasons = new List<string> { synthetic.Reason };
        var current = synthetic.DerivedFrom;

        for(var depth = 0; depth < MaxChainDepth; depth++)
        {
            switch(current)
            {
                case ParsedOrigin parsed:
                    var location = ComputeLocation(parsed.Source, span);
                    var context = string.Join(" -> ", reasons);
                    return (parsed.Source.Path, location, context);

                case TestOrigin:
                case null:
                    var testContext = string.Join(" -> ", reasons);
                    return ("<unknown>", default, testContext);

                case SyntheticOrigin next:
                    reasons.Add(next.Reason);
                    current = next.DerivedFrom;
                    break;

                default:
                    var fallbackContext = string.Join(" -> ", reasons);
                    return ("<unknown>", default, fallbackContext);
            }
        }

        // Max depth exceeded — circular chain protection
        var maxDepthContext = string.Join(" -> ", reasons);
        return ("<unknown>", default, maxDepthContext);
    }

    private static LinePositionSpan ComputeLocation(SourceText source, Storm.Runtime.SourceSpan span)
    {
        if(span.Start + span.Length > source.Contents.Length)
        {
            return default;
        }

        var sourceSpan = new SourceSpan(span.Start, span.Length);
        return source.ToLinePositionSpan(sourceSpan);
    }
}