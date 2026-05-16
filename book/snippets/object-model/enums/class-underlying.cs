using System;

namespace EnumDemo;

public class Label
{
    public string text;

    public int code;

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
}
