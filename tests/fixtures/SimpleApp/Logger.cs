namespace SimpleApp;

public static class Logger
{
    public static string Format(string who, string what)
    {
        return $"[{who}] {what}";
    }
}
