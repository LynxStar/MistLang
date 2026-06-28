using System;

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
        return $"Label({this.text}, {this.code})";
    }
}
public record class Severity(Label Value)
{
    public static readonly Severity Critical = new Severity(new Label("CRITICAL", 1));
    public static readonly Severity Warning = new Severity(new Label("WARNING", 2));
    public static readonly Severity Info = new Severity(new Label("INFO", 3));

    public static implicit operator Label(Severity e) => e.Value;

    public static explicit operator Severity?(Label value)
    {
        if (Equals(value, Critical.Value)) return Critical;
        else if (Equals(value, Warning.Value)) return Warning;
        else if (Equals(value, Info.Value)) return Info;
        return null;
    }

    public override string ToString()
    {
        if (ReferenceEquals(this, Critical)) return "Critical";
        else if (ReferenceEquals(this, Warning)) return "Warning";
        else if (ReferenceEquals(this, Info)) return "Info";
        return Value.ToString();
    }
}
