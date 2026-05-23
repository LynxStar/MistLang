using Standard;
using System;

namespace DefaultDemo;

public class Program
{
    public static void Main(string[] args)
    {
        int i = default(int);
        bool b = default(bool);
        string s = "";
        System.Console.WriteLine($"int={i} bool={b} string={s}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
