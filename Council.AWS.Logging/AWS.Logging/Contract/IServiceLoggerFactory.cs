using Microsoft.Extensions.Logging;

namespace AWS.Logging.Contract
{
    public interface IServiceLoggerFactory
    {
        ILogger GetLogger(string category);
    }

}
