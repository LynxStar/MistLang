using System;

namespace Snippet;

public class Account
{
    public string Name { get; private set; }

    public int Balance { get; private set; } = 0;

    public Account(string name)
    {
        this.Name = name;
    }

    public void Deposit(int amount)
    {
        this.Balance = this.Balance + amount;
    }
}
