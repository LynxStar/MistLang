using Standard;

namespace Snippet;

public class Style : Standard.IClonable<Snippet.Style>
{
    public string Font;
    public int Size;

    public Style(string font, int size)
    {
        this.Font = "";
        this.Size = default(int);
        this.Font = font;
        this.Size = size;
    }

    public Snippet.Style Clone()
    {
        var copy = (Snippet.Style)MemberwiseClone();
        return copy;
    }
}

public class Heading : Standard.IClonable<Snippet.Heading>
{
    public string Text;
    public Snippet.Style Look;

    public Heading(string text, int size)
    {
        this.Text = "";
        this.Text = text;
        this.Look = new Snippet.Style("serif", size);
    }

    public void Resize(int size)
    {
        this.Look.Size = size;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{this.Text} at {this.Look.Size}");
    }

    public Snippet.Heading Clone()
    {
        var copy = (Snippet.Heading)MemberwiseClone();
        copy.__CloneSlots_Heading();
        return copy;
    }

    protected void __CloneSlots_Heading()
    {
        this.Look = this.Look?.Clone()!;
    }
}

public class AutoCloneDemo
{
    public static void Run()
    {
        Snippet.Style shared = new Snippet.Style("sans", 10);
        Snippet.Style mine = shared.Clone();
        shared.Size = 11;
        System.Console.WriteLine(System.FormattableString.Invariant($"shared={shared.Size}, mine={mine.Size}"));
        Snippet.Heading original = new Snippet.Heading("Intro", 12);
        Snippet.Heading copy = original.Clone();
        copy.Resize(20);
        System.Console.WriteLine($"original={original.Describe()}, copy={copy.Describe()}");
    }

    public AutoCloneDemo()
    {
    }

    public static AutoCloneDemo __CreateDefault()
    {
        return new AutoCloneDemo();
    }
}
