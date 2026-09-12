namespace ReactiveLock.Integration.Shared;

public sealed class ConsoleWriterService(IWebHostEnvironment environment)
{
    public void WriteLine(string line)
    {
        if (!environment.IsProduction())
            Console.WriteLine(line);
    }
}
