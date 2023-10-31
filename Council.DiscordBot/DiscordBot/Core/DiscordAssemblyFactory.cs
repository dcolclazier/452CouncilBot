using Council.DiscordBot.Contract;
using System.Collections.Generic;
using System.Composition;
using System.Linq;

namespace Council.DiscordBot.Core
{
    [Export(typeof(IAssemblyFactory))]
    public class DiscordAssemblyFactory : IAssemblyFactory
    {
#if RELEASE
        private static readonly string[] assemblies = {
            $"/var/task\\MVP.DiscordBot.dll",
        };
#else
        private static readonly string[] assemblies = {
            @"C:\Development\MVP.452\MVP.DiscordBot\bin\MVP.DiscordBot.dll",
        };
#endif

        //todo - pull assemblies from s3 bucket for pluggability
        public IEnumerable<string> Assemblies() => assemblies.ToList().AsReadOnly();


        public DiscordAssemblyFactory() { }
    }

}
