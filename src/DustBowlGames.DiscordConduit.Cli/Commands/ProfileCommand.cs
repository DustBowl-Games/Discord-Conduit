using System.CommandLine;

namespace DustBowlGames.DiscordConduit.Cli.Commands;

internal static class ProfileCommand
{
    public static Command Create()
    {
        var command = new Command("profile") { Description = "Manage bot profiles" };

        // Add subcommand
        var addCommand = new Command("add") { Description = "Add a new bot profile" };
        var nameArg = new Argument<string>("name");
        var tokenOption = new Option<string>("--token") { Description = "Bot token", Required = true };
        addCommand.Arguments.Add(nameArg);
        addCommand.Options.Add(tokenOption);
        addCommand.SetAction(result =>
        {
            var name = result.GetValue(nameArg);
            Console.WriteLine($"Adding profile '{name}'...");
            // TODO: Wire up ProfileManager
            Console.WriteLine("Profile added.");
        });

        // List subcommand
        var listCommand = new Command("list") { Description = "List all bot profiles" };
        listCommand.SetAction(_ =>
        {
            Console.WriteLine("Profiles:");
            // TODO: Wire up ProfileManager
            Console.WriteLine("  (none)");
        });

        // Remove subcommand
        var removeCommand = new Command("remove") { Description = "Remove a bot profile" };
        var removeNameArg = new Argument<string>("name");
        removeCommand.Arguments.Add(removeNameArg);
        removeCommand.SetAction(result =>
        {
            var name = result.GetValue(removeNameArg);
            Console.WriteLine($"Removing profile '{name}'...");
            // TODO: Wire up ProfileManager
            Console.WriteLine("Profile removed.");
        });

        command.Subcommands.Add(addCommand);
        command.Subcommands.Add(listCommand);
        command.Subcommands.Add(removeCommand);

        return command;
    }
}
