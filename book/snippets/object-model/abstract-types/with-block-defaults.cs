using System;

namespace Snippet;

public interface ITaggable
{
    string Tag();
    string DisplayTag()
    {
        return $"[{this.Tag()}]";
    }
}
public class Widget : ITaggable
{
    public string GetWidgetTag()
    {
        return "widget";
    }

    string ITaggable.Tag() => this.GetWidgetTag();

    public string DisplayTag()
    {
        return $"[{this.Tag()}]";
    }

    public static Widget __CreateDefault()
    {
        var inst = new Widget();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var widget = new Widget();
        ITaggable taggable = widget;
        Console.WriteLine(taggable.Tag());
        Console.WriteLine(taggable.DisplayTag());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
