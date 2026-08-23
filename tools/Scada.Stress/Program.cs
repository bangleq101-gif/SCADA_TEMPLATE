using System.IO;

namespace Scada.Stress;

internal static class Program
{
    public static Task<int> Main(string[] args) => StressProgram.RunAsync(args);
}

public static class StressProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("--profile <RuntimeBaseline|HistorianHeavy|MqttHeavy|UiActive|CombinedWorstCase> --devices <n> --tags-per-device <n> --warmup-seconds <n> --measurement-seconds <n> --output <path> --instrumentation <true|false> --power-mode <value>");
            return 0;
        }
        try
        {
            var values = Parse(args);
            var profile = Enum.Parse<StressProfile>(Get(values, "profile", "RuntimeBaseline"), true);
            var output = Path.GetFullPath(Get(values, "output", Path.Combine(AppContext.BaseDirectory, "artifacts", "stress", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{profile}")));
            var settings = new StressRunSettings(profile,
                int.Parse(Get(values, "devices", "50")), int.Parse(Get(values, "tags-per-device", "200")),
                int.Parse(Get(values, "warmup-seconds", profile == StressProfile.CombinedWorstCase ? "120" : "60")),
                int.Parse(Get(values, "measurement-seconds", profile == StressProfile.CombinedWorstCase ? "600" : "300")),
                output, bool.Parse(Get(values, "instrumentation", "true")),
                int.Parse(Get(values, "seed", StressWorkloadFactory.DefaultSeed.ToString())),
                Enum.Parse<ValueChangePattern>(Get(values, "change-pattern", "EveryFourthRead"), true),
                Get(values, "power-mode", "AC"));
            var result = await new StressScenarioRunner().RunAsync(settings, ConsoleCancellation.Token);
            Console.WriteLine($"RESULT={Path.Combine(output, "result.json")}");
            Console.WriteLine($"PROFILE={result.Scenario} UPDATES_PER_SEC={result.Metrics.UpdatesPerSecond:F2} CLEAN={result.Correctness.CleanShutdown}");
            return result.Correctness.CleanShutdown ? 0 : 2;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length) throw new ArgumentException($"Invalid argument at position {index}.");
            values[args[index][2..]] = args[index + 1];
        }
        return values;
    }
    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;

    private static class ConsoleCancellation
    {
        public static CancellationToken Token { get; }
        static ConsoleCancellation()
        {
            var source = new CancellationTokenSource();
            Console.CancelKeyPress += (_, args) => { args.Cancel = true; source.Cancel(); };
            Token = source.Token;
        }
    }
}
