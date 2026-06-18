using System.CommandLine;
using DustBowlGames.DiscordConduit.Cli.Commands;
using DustBowlGames.DiscordConduit.Core.Migration;
using Serilog;

namespace DustBowlGames.DiscordConduit.Cli;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        var appDataPath = DustBowlGames.DiscordConduit.Core.IO.SecurePaths.CreateOwnerOnlyDirectory(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DiscordConduit"));

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(appDataPath, "logs", "conduit-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            await MigrationState.CleanupOldStatesAsync(appDataPath, Log.Logger);

            var rootCommand = new RootCommand("Discord Conduit — migrate Discord messages between channels and threads");

            rootCommand.Subcommands.Add(ProfileCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(MigrateCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(CloneCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(ExportCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(ValidateCommand.Create(appDataPath));
            rootCommand.Subcommands.Add(BotCommand.Create(appDataPath));

            return await rootCommand.Parse(args).InvokeAsync();
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
