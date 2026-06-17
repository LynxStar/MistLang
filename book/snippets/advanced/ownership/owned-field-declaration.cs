using System;

namespace Snippet;

public class Document
{
    public string Content;

    public int Version;

    public Document(string content, int version)
    {
        this.Content = content;
        this.Version = version;
    }

    public string Describe()
    {
        return $"Document(v{Version}: {Content})";
    }
}
public class OwnedFieldDeclarationDemo
{
    public static void Run()
    {
        var doc = new Document("Hello", 1);
        System.Console.WriteLine($"initial={doc.Describe()}");
        doc.Content = "Updated";
        System.Console.WriteLine($"updated={doc.Describe()}");
    }

    public static OwnedFieldDeclarationDemo __CreateDefault()
    {
        var inst = new OwnedFieldDeclarationDemo();
        return inst;
    }
}
