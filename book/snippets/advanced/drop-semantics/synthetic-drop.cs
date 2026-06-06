using System;

namespace Snippets;

public class Handle
{
    int Id;

    public Handle(int id)
    {
        this.Id = id;
    }

    public void Drop()
    {
        System.Console.WriteLine($"Handle({Id}) released");
    }

    protected Handle()
    {
        this.Id = 0;
    }

    public static Handle __CreateDefault()
    {
        var inst = new Handle();
        return inst;
    }
}
public class Session
{
    Handle Conn;

    Handle Log;

    public Session(int connId, int logId)
    {
        this.Conn = new Handle(connId);
        this.Log = new Handle(logId);
    }

    protected Session()
    {
        this.Conn = Handle.__CreateDefault();
        this.Log = Handle.__CreateDefault();
    }

    public static Session __CreateDefault()
    {
        var inst = new Session();
        return inst;
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
        var session = new Session(1, 2);
        try
        {
            System.Console.WriteLine("Session active");
        }
        finally
        {
            session.Drop();
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
