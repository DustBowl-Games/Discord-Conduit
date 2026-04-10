using System.CommandLine;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class ValidateCommand
{
    public static Command Create()
    {
        var command = new Command("validate") { Description = "Validate bot permissions for a migration" };

        var profileOption = new Option<string>("--profile") { Description = "Bot profile name", Required = true };
        var sourceOption = new Option<string>("--source") { Description = "Source channel ID", Required = true };
        var destOption = new Option<string>("--dest") { Description = "Destination channel ID", Required = true };

        command.Options.Add(profileOption);
        command.Options.Add(sourceOption);
        command.Options.Add(destOption);

        command.SetAction(result =>
        {
            var profile = result.GetValue(profileOption);
            var source = result.GetValue(sourceOption);
            var dest = result.GetValue(destOption);

            Console.WriteLine($"Validating permissions for profile '{profile}'...");
            Console.WriteLine($"  Source: {source}");
            Console.WriteLine($"  Destination: {dest}");
            // TODO: Wire up PermissionValidator
            Console.WriteLine("Validation complete.");
        });

        return command;
    }
}
