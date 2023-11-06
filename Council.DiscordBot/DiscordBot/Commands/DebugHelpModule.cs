using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Discord.Commands;
using System.Threading.Tasks;
using Council.DiscordBot.Core;

namespace Council.DiscordBot.Commands
{

    [DiscordCommand]
    public class DebugHelpModule : ModuleBase<SocketCommandContext>
    {

        [Command("ping")]
        [Alias("p")]
        [Summary("See if I'm responding to commands")]
        public async Task PingAsync()
        {
            await ReplyAsync("pong");
        }

        [Command("help")]
        [Alias("h")]
        [Summary("Show help")]
        public async Task HelpAsync()
        {
            var commandTypes = GetCommandTypes();
            var helpText = CreateHelpText(commandTypes);
            await ReplyAsync(helpText);
        }

        private IEnumerable<Type> GetCommandTypes()
        {

            List<Type> commandTypes = new List<Type>();
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var types = assembly.GetTypes().Where(t => Attribute.IsDefined(t, typeof(DiscordCommandAttribute)));
                commandTypes.AddRange(types);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not load assembly: {ex.Message}");
                // You might want to log this error or handle it according to your needs
            }

            return commandTypes;
        }

        private string CreateHelpText(IEnumerable<Type> commandTypes)
        {
            var helpText = new List<string>()
            {
                "Commands:"
            };

            foreach (var type in commandTypes)
            {
                foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<CommandAttribute>() != null))
                {
                    var commandAttribute = method.GetCustomAttribute<CommandAttribute>();
                    var summaryAttribute = method.GetCustomAttribute<SummaryAttribute>();

                    helpText.Add($"!{commandAttribute.Text}: {summaryAttribute?.Text ?? "Needs Summary"}");
                }
            }

            return string.Join("\n", helpText.Distinct());

        }
    }

}
