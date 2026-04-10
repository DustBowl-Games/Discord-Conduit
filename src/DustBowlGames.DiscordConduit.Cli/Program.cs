using System.CommandLine;
using DustBowlGames.DiscordConduit.Cli.Commands;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiscordConduit");

        Directory.CreateDirectory(appDataPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "conduit-.log"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            var rootCommand = new RootCommand("Discord Conduit — migrate Discord messages between channels and threads");

            rootCommand.Subcommands.Add(ProfileCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(MigrateCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(ValidateCommand.Create(appDataPath));

            return await rootCommand.Parse(args).InvokeAsync();
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
