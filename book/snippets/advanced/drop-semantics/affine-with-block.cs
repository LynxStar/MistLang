using System;

namespace Snippets;

public class Transaction
{
    string Id;

    public Transaction(string id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine($"Transaction({Id}) committed");
    }

    public string GetId()
    {
        return Id;
    }
}
public class Program
{
    public static void Main()
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var __with_0 = new Transaction("tx-001");
        MistContext.Push<Transaction>(__with_0);
        try
        {
            System.Console.WriteLine("Inside transaction");
        }
        finally
        {
            MistContext.Pop<Transaction>();
            __with_0.Drop();
        }
        var token = new Transaction("tx-002");
        System.Console.WriteLine($"Created {token.GetId()}, not dropping");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
