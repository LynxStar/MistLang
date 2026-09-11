using System;

namespace Snippets;

public class Connection
{
    private string Host;

    public Connection(string host)
    {
        this.Host = host;
    }

    public void Drop()
    {
        Console.WriteLine($"Connection({Host}) closed");
    }

    public string GetHost()
    {
        return Host;
    }
}

public class Program
{
    public static void Send(Snippets.Connection conn)
    {
        try
        {
            Console.WriteLine($"Sending on {conn.GetHost()}");
        }
        finally
        {
            conn.Drop();
        }
    }

    public static Snippets.Connection Upgrade(Snippets.Connection conn)
    {
        Console.WriteLine($"Upgrading {conn.GetHost()}");
        return conn;
    }

    public static void Main()
    {
        Snippets.Connection c = new Snippets.Connection("example.com");
        Send(c);
        Snippets.Connection c2 = new Snippets.Connection("other.com");
        Snippets.Connection c3 = Upgrade(c2);
        try
        {
        }
        finally
        {
            c3.Drop();
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
