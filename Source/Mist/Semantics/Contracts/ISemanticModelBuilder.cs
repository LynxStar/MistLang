using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Semantics.Contracts;

/// <summary>
/// Contract for the semantic model construction stage.
/// </summary>
public interface ISemanticModelBuilder
{
    ISemanticModel Build(
        Compilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        DiagnosticBag diagnostics);
}
