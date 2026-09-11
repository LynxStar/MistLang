using System;

async System.Threading.Tasks.Task<int> FetchData()
{
    return 100;
}
async System.Threading.Tasks.Task<string> FormatData(int value)
{
    return "Value: " + value.ToString();
}
async System.Threading.Tasks.Task Main()
{
    int result = await FetchData();
    string formatted = await FormatData(result);
    Console.WriteLine(formatted);
}
