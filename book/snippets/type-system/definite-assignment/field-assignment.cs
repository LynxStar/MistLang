using Standard;

namespace Snippet;

public class Connection
{
    public string Host;
    public int Port;

    public Connection(string host, int port)
    {
        this.Host = host;
        this.Port = port;
    }

    public string Describe()
    {
        return $"{Host}:{Port}";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Connection conn = new Snippet.Connection("localhost", 8080);
        System.Console.WriteLine(conn.Describe());
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
