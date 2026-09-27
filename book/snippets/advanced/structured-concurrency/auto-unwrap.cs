using Standard;

async System.Threading.Tasks.Task<int> FetchData()
{
    return 100;
}
async System.Threading.Tasks.Task<string> FormatData(int value)
{
    return "Value: " + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
async System.Threading.Tasks.Task Main()
{
    int result = await FetchData();
    string formatted = await FormatData(result);
    System.Console.WriteLine(formatted);
}
