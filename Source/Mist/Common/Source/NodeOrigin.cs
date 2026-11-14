namespace Mist.Common.Source;

public abstract record NodeOrigin;

public sealed record ParsedOrigin(SourceText Source) : NodeOrigin;

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

/// Test nodes have no source file.
public sealed record TestOrigin : NodeOrigin
{
    public static readonly TestOrigin Instance = new();
    private TestOrigin() { }
}