using System.CommandLine;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class MigrateCommand
{
    public static Command Create()
    {
        var command = new Command("migrate") { Description = "Run a message migration" };

        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        var sourceOption = new Option<string>("--source") { Description = "Source channel ID", Required = true };
        var destOption = new Option<string>("--dest") { Description = "Destination channel ID", Required = true };
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Validate without posting" };
        var noReactionsOption = new Option<bool>("--no-reactions") { Description = "Skip reaction migration" };

        command.Options.Add(profileOption);
        command.Options.Add(sourceOption);
        command.Options.Add(destOption);
        command.Options.Add(dryRunOption);
        command.Options.Add(noReactionsOption);

        command.SetAction(result =>
        {
            var profile = result.GetValue(profileOption);
            var source = result.GetValue(sourceOption);
            var dest = result.GetValue(destOption);
            var dryRun = result.GetValue(dryRunOption);
            var noReactions = result.GetValue(noReactionsOption);

            Console.WriteLine($"Migrating from {source} to {dest} using profile '{profile}'...");
            if (dryRun) Console.WriteLine("  (dry run mode)");
            if (noReactions) Console.WriteLine("  (skipping reactions)");
            // TODO: Wire up MigrationEngine
            Console.WriteLine("Migration complete.");
        });

        // Resume subcommand
        var resumeCommand = new Command("resume") { Description = "Resume an interrupted migration" };
        var stateFileArg = new Argument<string>("state-file");
        resumeCommand.Arguments.Add(stateFileArg);
        resumeCommand.SetAction(result =>
        {
            var stateFile = result.GetValue(stateFileArg);
            Console.WriteLine($"Resuming migration from {stateFile}...");
            // TODO: Wire up MigrationEngine.ResumeAsync
            Console.WriteLine("Migration complete.");
        });

        command.Subcommands.Add(resumeCommand);

        return command;
    }
}
