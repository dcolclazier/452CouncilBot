using Microsoft.Extensions.Logging;
using System;
using AWS.Logger;
using System.Reflection;
using System.Composition;
using MVP.AWS.Logging.Contract;
using System.IO;

namespace MVP.AWS.Logging
{
    [Export(typeof(IServiceLoggerFactory))]
    [Shared]
    sealed class ServiceLoggerFactory : IServiceLoggerFactory
    {
        private ILoggerFactory _factory;
        public ServiceLoggerFactory()
        {
            var currentLevel = LogLevel.Trace;
            _factory = new LoggerFactory();

            var loggingLevelStr = Environment.GetEnvironmentVariable("LOGGING_LEVEL");
            if (!string.IsNullOrWhiteSpace(loggingLevelStr) && int.TryParse(loggingLevelStr, out var level) && level < 7 && level > 0)
            {
                currentLevel = (LogLevel)level;
            }
            Configure(_factory, currentLevel);

        }

        private void Configure(ILoggerFactory factory, LogLevel currentLevel)
        {
#if DEBUG
            bool logDirect = true;
#else
            bool logDirect = false;
            var logDirectStr = Environment.GetEnvironmentVariable("LOGGING_CLOUDWATCH");
            if (!string.IsNullOrEmpty(logDirectStr) && int.TryParse(logDirectStr, out var logDirectVal) && logDirectVal > 0)
            {
                logDirect = true;
            }
#endif
            if (logDirect)
            {
                var config = new AWSLoggerConfig()
                {
                    LibraryLogFileName = Path.Combine(Path.GetTempPath(), "aws-logger-errors.txt")
                };

                var group = Environment.GetEnvironmentVariable("LOGGING_CLOUDWATCH_GROUP");
#if DEBUG
                config.LogGroup = "MVP/default/debug";
#else
                config.LogGroup = string.IsNullOrEmpty(group) ? "MVP/default" : group;
#endif

                var batchPushSecsStr = Environment.GetEnvironmentVariable("LOGGING_CLOUDWATCH_BATCHPUSHSECS");
                if (!string.IsNullOrEmpty(batchPushSecsStr) && int.TryParse(batchPushSecsStr, out var batchPushSecs) && batchPushSecs > 0)
                {
                    config.BatchPushInterval = new TimeSpan(0, 0, batchPushSecs);
                }

                var batchPushBytesStr = Environment.GetEnvironmentVariable("LOGGING_CLOUDWATCH_BATCHPUSHBYTES");
                if (!string.IsNullOrEmpty(batchPushBytesStr) && int.TryParse(batchPushBytesStr, out var batchPushBytes) && batchPushBytes > 0)
                {
                    config.BatchSizeInBytes = batchPushBytes;
                }
                _factory.AddAWSProvider(config, currentLevel);
            }
            else
            {
                _factory.AddLambdaLogger(new LambdaLoggerOptions
                {
                    Filter = (s, logLevel) => logLevel >= currentLevel
                });
            }


        }
        public ILogger GetLogger(string category)
        {
            return _factory.CreateLogger(category + " - " + Assembly.GetExecutingAssembly().GetName().Version.ToString());
        }
    }
}
