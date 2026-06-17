using System;

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
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var conn = new Connection("localhost", 8080);
        Console.WriteLine($"host={conn.Host} port={conn.Port} secure={conn.Secure}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
