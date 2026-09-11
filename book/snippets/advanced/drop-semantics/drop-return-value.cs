using System;

namespace Snippets;

public class SecretBox
{
    private string Secret;

    public SecretBox(string secret)
    {
        this.Secret = secret;
    }

    public string Drop()
    {
        Console.WriteLine("SecretBox consumed");
        return Secret;
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.SecretBox box = new Snippets.SecretBox("password123");
        string secret = box.Drop();
        Console.WriteLine($"Got: {secret}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
