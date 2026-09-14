using Standard;

namespace Snippet;

public class Account
{
    public Snippet.UserId Owner = ((Snippet.UserId)(1));

    public Snippet.UserId Manager { get; set; } = ((Snippet.UserId)(2));

    public static Snippet.UserId Guest()
    {
        return ((Snippet.UserId)(0));
    }

    public Account()
    {
        this.Owner = ((Snippet.UserId)(1));
    }

    public static Account __CreateDefault()
    {
        return new Account();
    }
}

public class Program
{
    private static string Describe(Snippet.UserId id)
    {
        return $"user {id}";
    }

    public static void Main(string[] args)
    {
        Snippet.UserId id = ((Snippet.UserId)(7));
        id = ((Snippet.UserId)(8));
        Snippet.UserId[] team = new Snippet.UserId[] { ((Snippet.UserId)(10)), ((Snippet.UserId)(11)) };
        Snippet.Account account = new Snippet.Account();
        System.Console.WriteLine($"{Describe(id)} team={team[0]},{team[1]}");
        System.Console.WriteLine($"owner={account.Owner} manager={account.Manager} guest={Snippet.Account.Guest()}");
        System.Console.WriteLine(Describe(((Snippet.UserId)(9))));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
