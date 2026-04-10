using System.CommandLine;
using DustBowlGames.DiscordConduit.Cli.Commands;

namespace DustBowlGames.DiscordConduit.Cli;

internal sealed class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Discord Conduit — migrate Discord messages between channels and threads");

        rootCommand.Subcommands.Add(ProfileCommand.Create());
        rootCommand.Subcommands.Add(MigrateCommand.Create());
        rootCommand.Subcommands.Add(ValidateCommand.Create());

        return await rootCommand.Parse(args).InvokeAsync();
    }
}
