using Standard;

namespace Snippet;

public class Logger
{
    internal string prefix;

    public Logger(string prefix)
    {
        this.prefix = "";
        this.prefix = prefix;
    }

    public string Format(string message)
    {
        return $"[{prefix}] {message}";
    }
}

public class LoggerFactory
{
    public static string GetPrefix(Snippet.Logger logger)
    {
        return logger.prefix;
    }

    public LoggerFactory()
    {
    }

    public static LoggerFactory __CreateDefault()
    {
        return new LoggerFactory();
    }
}
