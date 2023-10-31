using Microsoft.Extensions.Logging;

namespace MVP.AWS.Logging.Contract
{
    public interface IServiceLoggerFactory
    {
        ILogger GetLogger(string category);
    }

}
