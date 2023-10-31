using Microsoft.Extensions.Logging;
using MVP.AWS.Logging.Contract;
using MEF.NetCore;
using System.Composition;

namespace AWS.Logging
{
    public class LoggingResource
    {
        [Import]
        private IServiceLoggerFactory LogFactory { get; set; } = null;

        private ILogger _logger = null;
        private string _name;
        protected ILogger Logger => _logger ?? (_logger = LogFactory.GetLogger(_name));

        protected LoggingResource(string name)
        {
            _name = name;
            MEFLoader.SatisfyImportsOnce(this);
        }
    }
}
