using Council.DiscordBot.Contract;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;

namespace Council.DiscordBot.Core
{
    [Export(typeof(IAssemblyFactory))]
    public class DiscordAssemblyFactory : IAssemblyFactory
    {
        private static readonly string[] assemblies = {
            $"DiscordBot.Commands.dll",
        };

        private static readonly string _assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        //todo - pull assemblies from s3 bucket for pluggability
        public IEnumerable<string> Assemblies()
        {
            var list = new List<string>();
            foreach(var assembly in assemblies)
            {
                list.AddRange(Directory.GetFiles(_assemblyPath, assembly));
            }
            return list.AsReadOnly();
        }


        public DiscordAssemblyFactory() { }
    }

}
