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
}

public class FieldTypeRestrictionDemo
{
    public static void Run()
    {
        Snippet.Vault vault = new Snippet.Vault("top-secret");
        Console.WriteLine($"initial={vault.Describe()}");
        vault.RotateSecret("new-secret");
        Console.WriteLine($"rotated={vault.Describe()}");
    }

    public FieldTypeRestrictionDemo()
    {
    }

    public static FieldTypeRestrictionDemo __CreateDefault()
    {
        return new FieldTypeRestrictionDemo();
    }
}
