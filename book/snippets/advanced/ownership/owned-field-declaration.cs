using Standard;

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
        return System.FormattableString.Invariant($"Document(v{Version}: {Content})");
    }
}

public class OwnedFieldDeclarationDemo
{
    public static void Run()
    {
        Snippet.Document doc = new Snippet.Document("Hello", 1);
        System.Console.WriteLine($"initial={doc.Describe()}");
        doc.Content = "Updated";
        System.Console.WriteLine($"updated={doc.Describe()}");
    }

    public OwnedFieldDeclarationDemo()
    {
    }

    public static OwnedFieldDeclarationDemo __CreateDefault()
    {
        return new OwnedFieldDeclarationDemo();
    }
}
