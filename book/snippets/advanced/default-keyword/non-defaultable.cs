using Standard;

namespace Snippet;

public class DatabaseConnection
{
    public string ConnectionString;
    public int Timeout;

    public DatabaseConnection(string connStr, int timeout)
    {
        this.ConnectionString = connStr;
        this.Timeout = timeout;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.DatabaseConnection db = new Snippet.DatabaseConnection("Server=localhost", 30);
        System.Console.WriteLine(System.FormattableString.Invariant($"conn={db.ConnectionString} timeout={db.Timeout}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
