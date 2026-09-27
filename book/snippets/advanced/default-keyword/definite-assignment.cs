using Standard;

namespace Snippet;

public class Connection
{
    public string Host;
    public int Port;
    public bool Secure;

    public Connection(string host, int port)
    {
        this.Host = host;
        this.Port = port;
        this.Secure = false;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Connection conn = new Snippet.Connection("localhost", 8080);
        System.Console.WriteLine(System.FormattableString.Invariant($"host={conn.Host} port={conn.Port} secure={conn.Secure}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
