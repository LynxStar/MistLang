using System;

namespace Snippets;

public class SecretBox
{
    string Secret;

    public SecretBox(string secret)
    {
        this.Secret = secret;
    }

    public string Drop()
    {
        System.Console.WriteLine("SecretBox consumed");
        return Secret;
    }
}
public class Program
{
    public static void Main()
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var box = new SecretBox("password123");
        var secret = box.Drop();
        System.Console.WriteLine($"Got: {secret}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
