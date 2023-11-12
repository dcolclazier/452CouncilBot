using AWS.Logging;
using DiscordBot.Core.Contract;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Council.DiscordBot.Core
{
    [Export(typeof(IAssemblyFactory))]
    public class DiscordAssemblyFactory : LoggingResource, IAssemblyFactory
    {
        private static readonly string[] assemblies = {
            $"/opt/discordbot/DiscordBot.Commands.dll",
        };

        private static readonly string _assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        //todo - pull assemblies from s3 bucket for pluggability
        public IEnumerable<string> Assemblies() => assemblies.ToList().AsReadOnly();

        public DiscordAssemblyFactory(): base(nameof(DiscordAssemblyFactory)) { }
    }

}
