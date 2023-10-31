using System.Collections.Generic;

namespace Council.DiscordBot.Contract
{
    public interface IAssemblyFactory
    {
        IEnumerable<string> Assemblies();
    }



}
