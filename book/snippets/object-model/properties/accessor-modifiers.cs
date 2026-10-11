using Standard;

namespace Snippet;

public class Account
{
    public Account(string name)
    {
        this.Name = "";
        this.Name = name;
    }

    public string Name { get; private set; }

    public int Balance { get; private set; } = 0;

    public void Deposit(int amount)
    {
        this.Balance = this.Balance + amount;
    }
}
