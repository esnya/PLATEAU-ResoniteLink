namespace Plateau.ResoniteLink.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return CliApplication.CreateDefault().RunAsync(args);
    }
}
