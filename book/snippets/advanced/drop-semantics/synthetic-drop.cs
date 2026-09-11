using System;

namespace Snippets;

public class Handle
{
    private int Id;

    public Handle(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        Console.WriteLine($"Handle({Id}) released");
    }
}

public class Session
{
    private Snippets.Handle Conn;
    private Snippets.Handle Log;

    public Session(int connId, int logId)
    {
        this.Conn = new Snippets.Handle(connId);
        this.Log = new Snippets.Handle(logId);
    }

    public void Drop()
    {
        this.Conn.Drop();
        this.Log.Drop();
    }
}

public class Program
{
    public static void Main()
    {
        Snippets.Session session = new Snippets.Session(1, 2);
        try
        {
            Console.WriteLine("Session active");
        }
        finally
        {
            session.Drop();
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
