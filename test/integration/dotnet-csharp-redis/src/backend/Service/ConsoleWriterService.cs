public class ConsoleWriterService
{
    private IWebHostEnvironment Env { get; }
    public ConsoleWriterService(IWebHostEnvironment env)
    {
        Env = env;        
    }
    public void WriteLine(string line)
    {
        if (Env.IsProduction())
            return;
        Console.WriteLine(line);
    }
}