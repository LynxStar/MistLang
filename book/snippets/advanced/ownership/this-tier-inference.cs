using System;

namespace Snippet;

public class Account
{
    public string Name;
    public int Balance;

    public Account(string name, int balance)
    {
        this.Name = name;
        this.Balance = balance;
    }

    public void Deposit(int amount)
    {
        this.Balance = this.Balance + amount;
    }

    public string Summary()
    {
        return $"Account({Name}, balance={Balance})";
    }
}

public class ThisTierInferenceDemo
{
    public static void Run()
    {
        Snippet.Account acct = new Snippet.Account("Alice", 100);
        Console.WriteLine($"summary={acct.Summary()}");
        acct.Deposit(50);
        Console.WriteLine($"after deposit={acct.Summary()}");
        Snippet.Account shared = new Snippet.Account("Bob", 200);
        Console.WriteLine($"shared summary={shared.Summary()}");
    }

    public ThisTierInferenceDemo()
    {
    }

    public static ThisTierInferenceDemo __CreateDefault()
    {
        return new ThisTierInferenceDemo();
    }
}
