using Microsoft.Extensions.Logging;
using MEF.NetCore;
using System.Composition;
using AWS.Logging.Contract;

namespace AWS.Logging
{
    public class LoggingResource
    {
        [Import]
        private IServiceLoggerFactory LogFactory { get; set; } = null;

        private ILogger _logger = null;
        private readonly string _name;
        protected ILogger Logger => _logger ?? (_logger = LogFactory.GetLogger(_name));

        protected LoggingResource(string name)
        {
            _name = name;
            MEFLoader.SatisfyImportsOnce(this);
        }
    }
}
