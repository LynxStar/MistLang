using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Every <c>?</c> the source applied and the type it was applied to. Resolution flattens
/// <c>(int | void)?</c> into the same union as <c>int?</c>, which is coherent to build on and
/// leaves nothing behind to judge; the rule that refuses the suffix over a type already holding
/// void needs the application itself, and this is where it survives.
/// </summary>
internal sealed class OptionalApplicationRegistry
{
    private readonly List<OptionalApplication> applications = [];

    public IReadOnlyList<OptionalApplication> Applications => applications;

    public void Record(IMistType inner, SyntaxNode node)
    {
        applications.Add(new OptionalApplication(inner, node));
    }
}

internal sealed record OptionalApplication(IMistType Inner, SyntaxNode Node);