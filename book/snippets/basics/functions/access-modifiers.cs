using System;

namespace Demo;

public class Account
{
    private int balance;

    public Account(int initial)
    {
        this.balance = initial;
    }

    private int ApplyFee(int amount)
    {
        return amount - 1;
    }

    public void Deposit(int amount)
    {
        this.balance = this.balance + ApplyFee(amount);
    }

    public int GetBalance()
    {
        return this.balance;
    }

    public static void Main(string[] args)
    {
        var acct = new Account(100);
        acct.Deposit(50);
        Console.WriteLine($"Balance: {acct.GetBalance()}");
    }

    protected Account()
    {
        this.balance = 0;
    }

    public static Account __CreateDefault()
    {
        var inst = new Account();
        return inst;
    }
}
