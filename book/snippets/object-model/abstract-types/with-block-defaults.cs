using Standard;

namespace Snippet;

public interface ITaggable
{
    string Tag();

    string DisplayTag()
    {
        return $"[{this.Tag()}]";
    }
}

public class Widget : Snippet.ITaggable
{
    public string GetWidgetTag()
    {
        return "widget";
    }

    public Widget()
    {
    }

    public static Widget __CreateDefault()
    {
        return new Widget();
    }

    string Snippet.ITaggable.Tag() => this.GetWidgetTag();
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Widget widget = new Snippet.Widget();
        Snippet.ITaggable taggable = widget;
        System.Console.WriteLine(taggable.Tag());
        System.Console.WriteLine(taggable.DisplayTag());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
