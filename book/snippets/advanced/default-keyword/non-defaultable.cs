using System;

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
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var db = new DatabaseConnection("Server=localhost", 30);
        Console.WriteLine($"conn={db.ConnectionString} timeout={db.Timeout}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
