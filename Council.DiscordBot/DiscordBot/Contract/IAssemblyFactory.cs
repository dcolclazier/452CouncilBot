using System.Collections.Generic;

namespace MVP.DiscordBot.Contract
{
    public interface IAssemblyFactory
    {
        IEnumerable<string> Assemblies();
    }



}
