using System.Collections.Generic;

namespace DiscordBot.Core.Contract
{
    public interface IAssemblyFactory
    {
        IEnumerable<string> Assemblies();
    }
}