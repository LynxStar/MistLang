using Namespaces.Models;
using Namespaces.Models.Game;
using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        var player = new Player("Alice", 100);
        System.Console.WriteLine(player.Describe());
        var state = new GameState(1, true);
        System.Console.WriteLine(state.Summary());
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
