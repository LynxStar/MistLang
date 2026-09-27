using Standard;

namespace Snippet;

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
        Snippet.Account acct = new Snippet.Account(100);
        acct.Deposit(50);
        System.Console.WriteLine(System.FormattableString.Invariant($"Balance: {acct.GetBalance()}"));
    }
}
