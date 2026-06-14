using System;

namespace Snippet;

public class Vault
{
    public string Secret;

    public int AccessCount;

    public Vault(string secret)
    {
        this.Secret = secret;
        this.AccessCount = 0;
    }

    public void RotateSecret(string newSecret)
    {
        this.Secret = newSecret;
        this.AccessCount = this.AccessCount + 1;
    }

    public string Describe()
    {
        return $"Vault(accesses={AccessCount}, secret={Secret})";
    }

    protected Vault()
    {
        this.Secret = "";
        this.AccessCount = 0;
    }

    public static Vault __CreateDefault()
    {
        var inst = new Vault();
        return inst;
    }
}
public class FieldTypeRestrictionDemo
{
    public static void Run()
    {
        var vault = new Vault("top-secret");
        System.Console.WriteLine($"initial={vault.Describe()}");
        vault.RotateSecret("new-secret");
        System.Console.WriteLine($"rotated={vault.Describe()}");
    }

    public static FieldTypeRestrictionDemo __CreateDefault()
    {
        var inst = new FieldTypeRestrictionDemo();
        return inst;
    }
}
