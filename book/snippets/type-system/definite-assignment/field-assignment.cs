using System;

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
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var conn = new Connection("localhost", 8080);
        System.Console.WriteLine(conn.Describe());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
