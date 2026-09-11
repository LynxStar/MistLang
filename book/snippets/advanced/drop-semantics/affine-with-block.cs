using System;

namespace Snippets;

public class Transaction
{
    private string Id;

    public Transaction(string id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        Console.WriteLine($"Transaction({Id}) committed");
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
        {
            var __e1 = new Snippets.Transaction("tx-001");
            MistContext.Push<Snippets.Transaction>(__e1);
            try
            {
                Console.WriteLine("Inside transaction");
            }
            finally
            {
                MistContext.Pop<Snippets.Transaction>();
                __e1.Drop();
            }
        }
        Snippets.Transaction token = new Snippets.Transaction("tx-002");
        Console.WriteLine($"Created {token.GetId()}, not dropping");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
