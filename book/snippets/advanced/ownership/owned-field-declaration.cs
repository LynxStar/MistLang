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
        Snippet.Document doc = new Snippet.Document("Hello", 1);
        Console.WriteLine($"initial={doc.Describe()}");
        doc.Content = "Updated";
        Console.WriteLine($"updated={doc.Describe()}");
    }

    public OwnedFieldDeclarationDemo()
    {
    }

    public static OwnedFieldDeclarationDemo __CreateDefault()
    {
        return new OwnedFieldDeclarationDemo();
    }
}
