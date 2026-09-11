using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        int day = 3;
        string dayName = day switch { 1 => "Monday", 2 => "Tuesday", 3 => "Wednesday", 4 => "Thursday", 5 => "Friday", var _ => "Weekend" };
        Console.WriteLine($"Day {day} is {dayName}");
        string command = "quit";
        string response = command switch { "start" => "Starting...", "stop" => "Stopping...", "quit" => "Goodbye!", var _ => "Unknown command" };
        Console.WriteLine($"Command '{command}' => {response}");
        bool isActive = true;
        string status = isActive switch { true => "Active", false => "Inactive" };
        Console.WriteLine($"isActive={isActive} => {status}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
