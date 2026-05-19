using System;
using System.Threading.Tasks;

async Task<int> FetchData()
{
    return 100;
}

async Task<string> FormatData(int value)
{
    return $"Value: {value}";
}

// Sync method in Mist becomes async in C# due to auto-unwrap
async Task Main()
{
    // Compiler inserts await for each async call
    var result = await FetchData();           // result: int
    var formatted = await FormatData(result); // formatted: string

    Console.WriteLine(formatted);  // "Value: 100"
}
