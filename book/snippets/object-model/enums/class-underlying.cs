using Standard;

namespace Snippet;

public class Label
{
    public readonly string text;
    public readonly int code;

    public Label(string text, int code)
    {
        this.text = text;
        this.code = code;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Label({this.text}, {this.code})");
    }
}

public sealed record class Severity(Snippet.Label Value)
{
    public static readonly Severity Critical = new Severity(new Snippet.Label("CRITICAL", 1));
    public static readonly Severity Warning = new Severity(new Snippet.Label("WARNING", 2));
    public static readonly Severity Info = new Severity(new Snippet.Label("INFO", 3));

    public static implicit operator Snippet.Label(Severity e) => e.Value;

    public static explicit operator Severity?(Snippet.Label value)
    {
        if(Equals(value, Severity.Critical.Value)) return Severity.Critical;
        if(Equals(value, Severity.Warning.Value)) return Severity.Warning;
        if(Equals(value, Severity.Info.Value)) return Severity.Info;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Severity.Critical)) return "Critical";
        if(ReferenceEquals(this, Severity.Warning)) return "Warning";
        if(ReferenceEquals(this, Severity.Info)) return "Info";
        return Value.ToString();
    }
}
