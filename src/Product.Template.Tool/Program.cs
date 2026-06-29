namespace Product.Template.Tool;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await ToolRunner.RunAsync(args, Console.Out, Console.Error);
    }
}
