using System;

namespace Snippets;

public class Connection
{
    string Host;

    public Connection(string host)
    {
        this.Host = host;
    }

    public void Drop()
    {
        System.Console.WriteLine($"Connection({Host}) closed");
    }

    public string GetHost()
    {
        return Host;
    }

    protected Connection()
    {
        this.Host = "";
    }

    public static Connection __CreateDefault()
    {
        var inst = new Connection();
        return inst;
    }
}
public class Program
{
    public static void Send(Connection conn)
    {
        try
        {
            System.Console.WriteLine($"Sending on {conn.GetHost()}");
        }
        finally
        {
            conn.Drop();
        }
    }

    public static Connection Upgrade(Connection conn)
    {
        System.Console.WriteLine($"Upgrading {conn.GetHost()}");
        return conn;
    }

    public static void Main()
    {
        var c = new Connection("example.com");
        Send(c);
        var c2 = new Connection("other.com");
        var c3 = Upgrade(c2);
        try
        {
        }
        finally
        {
            c3.Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
