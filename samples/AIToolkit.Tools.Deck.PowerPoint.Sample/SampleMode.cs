internal enum SampleMode
{
    Interactive,
    TemplateCreate,
}

internal static class SampleModeParser
{
    public static SampleMode Parse(string[] args)
    {
        if (TryParseArgs(args, out var sampleMode))
        {
            return sampleMode;
        }

        return PromptForMode();
    }

    public static string GetUsage() =>
        "Sample modes: 1 = interactive, 2 = template-create. You can also pass --sample template-create when you need a non-interactive launch.";

    private static bool TryParseArgs(string[] args, out SampleMode sampleMode)
    {
        sampleMode = SampleMode.Interactive;
        if (args.Length == 0)
        {
            return false;
        }

        string? modeToken = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--sample", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[index], "--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException("Expected a sample mode after --sample/--mode.");
                }

                modeToken = args[index + 1];
                break;
            }
        }

        modeToken ??= args[0];
        sampleMode = Normalize(modeToken);
        return true;
    }

    private static SampleMode PromptForMode()
    {
        while (true)
        {
            Console.WriteLine("Select a sample mode:");
            Console.WriteLine("1. Interactive chat sample");
            Console.WriteLine("2. Template creation sample");
            Console.Write("Choice [1]: ");

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input) || string.Equals(input, "1", StringComparison.Ordinal))
            {
                Console.WriteLine();
                return SampleMode.Interactive;
            }

            if (string.Equals(input, "2", StringComparison.Ordinal))
            {
                Console.WriteLine();
                return SampleMode.TemplateCreate;
            }

            try
            {
                var mode = Normalize(input);
                Console.WriteLine();
                return mode;
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Invalid selection. Enter 1 or 2.");
                Console.WriteLine();
            }
        }
    }

    private static SampleMode Normalize(string modeToken) =>
        modeToken.Trim().ToLowerInvariant() switch
        {
            "interactive" or "chat" => SampleMode.Interactive,
            "template" or "template-create" or "template-generation" => SampleMode.TemplateCreate,
            _ => throw new InvalidOperationException($"Unsupported sample mode '{modeToken}'. {GetUsage()}"),
        };
}