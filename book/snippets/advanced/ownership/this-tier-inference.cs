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

    protected Account()
    {
        this.Name = "";
        this.Balance = 0;
    }

    public static Account __CreateDefault()
    {
        var inst = new Account();
        return inst;
    }
}
public class ThisTierInferenceDemo
{
    public static void Run()
    {
        var acct = new Account("Alice", 100);
        System.Console.WriteLine($"summary={acct.Summary()}");
        acct.Deposit(50);
        System.Console.WriteLine($"after deposit={acct.Summary()}");
        var shared = new Account("Bob", 200);
        System.Console.WriteLine($"shared summary={shared.Summary()}");
    }

    public static ThisTierInferenceDemo __CreateDefault()
    {
        var inst = new ThisTierInferenceDemo();
        return inst;
    }
}
