namespace Mist.Common.Source;

/// <summary>
/// Represents the origin of an AST node — where it came from.
/// </summary>
public abstract record NodeOrigin;

/// <summary>
/// The node was produced by parsing a real source file.
/// </summary>
public sealed record ParsedOrigin(SourceText Source) : NodeOrigin;

/// <summary>
/// The node was synthesized by a compiler pass (e.g., desugaring).
/// </summary>
public sealed record SyntheticOrigin : NodeOrigin
{
    public NodeOrigin DerivedFrom { get; }
    public string Reason { get; }

    public SyntheticOrigin(NodeOrigin DerivedFrom, string Reason) : base()
    {
        if(string.IsNullOrEmpty(Reason))
        {
            throw new ArgumentException("Reason cannot be null or empty.", nameof(Reason));
        }

        this.DerivedFrom = DerivedFrom;
        this.Reason = Reason;
    }
}

/// <summary>
/// The node was constructed in a test — no source file exists.
/// </summary>
public sealed record TestOrigin : NodeOrigin
{
    public static readonly TestOrigin Instance = new();
    private TestOrigin() { }
}