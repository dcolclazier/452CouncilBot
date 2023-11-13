using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using AWS.Logging;
using MEF.NetCore;
using Microsoft.Extensions.Logging;
using System.Composition;
using DiscordBot.Core;
using DiscordBot.Core.Contract;
using Newtonsoft.Json;

namespace Council.DiscordBot.Core
{

    [AttributeUsage(AttributeTargets.Method)]
    public class DiscordEventHandlerAttribute : Attribute
    {
        public string EventName { get; }

        public DiscordEventHandlerAttribute(string eventName)
        {
            EventName = eventName;
        }
    }


    //[Export(typeof(IConnectionService))]
    //[Shared]
    public class DiscordConnectionService : LoggingResource, IConnectionService
    {
        public DiscordSocketClient Client { get; private set; }
        public CommandService Commands { get; private set; }
        public ServiceProvider Services { get; private set; }


        private Timer? _timer;
        private EventWaitHandle? _waitHandle;

        [Import] private IAssemblyFactory _assemblyFactory { get; set; }
        public bool PendingDisconnect { get; private set; }


        public DiscordConnectionService() : base(nameof(DiscordConnectionService))
        {

            MEFLoader.SatisfyImportsOnce(this);
            Client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Debug,
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent

            });
            //Client.Log += async (message) => await Task.Run(() => Logger.LogInformation($"LOG: {JsonConvert.SerializeObject(message)}"));

            Commands = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = true,
                DefaultRunMode = RunMode.Async,
                LogLevel = LogSeverity.Debug,
                IgnoreExtraArgs = true
            });

        }

        public async Task InitializeAsync(Func<Task> OnReady, string token, int timeToRun, EventWaitHandle waitHandle)
        {
            if (waitHandle != null)
            {
                _waitHandle = waitHandle;
            }
            Logger.LogInformation("InitializeAsync");
            if (timeToRun > 0) StartTimer(timeToRun);

            await RemoveAllCommandsAsync();

            var serviceCollection = new ServiceCollection().AddSingleton(Client).AddSingleton(Commands);
            Client.MessageReceived += OnMessageReceived;
            Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(method => method.GetCustomAttribute<DiscordEventHandlerAttribute>() != null)
                .ToList()
                .ForEach(method =>
                {
                    Logger.LogInformation($"Found Event to add... {method.Name}");
                    var attribute = method.GetCustomAttribute<DiscordEventHandlerAttribute>();

                    var eventInfo = Client.GetType().GetEvent(attribute.EventName);
                    if (eventInfo != null)
                    {
                        // Subscribe to the event
                        eventInfo.AddEventHandler(
                            Client, 
                            Delegate.CreateDelegate(eventInfo.EventHandlerType, Activator.CreateInstance(method.DeclaringType), 
                            method.Name
                        ));
                        Logger.LogInformation($"Subscribed {method.Name} to DiscordSocketClient.{eventInfo.Name}");
                    }
                    else
                    {
                        Logger.LogWarning($"Could not find event '{attribute.EventName}' in DiscordSocketClient.");
                    }
                });


            foreach (var ass in _assemblyFactory.Assemblies().ToList())
            {
                var assembly = Assembly.LoadFile(ass);
                var innerServiceCollection = new ServiceCollection().AddSingleton(Client).AddSingleton(Commands);
                assembly.GetTypes()
                   .Where(t => t.GetCustomAttribute<DiscordCommandAttribute>(true) is DiscordCommandAttribute ||
                            t.GetCustomAttribute<ExportAttribute>(true) is ExportAttribute).ToList()
                   .ForEach(s =>
                   {
                       Logger.LogInformation($"Found Command Class: {s.Name}");
                       innerServiceCollection = innerServiceCollection.AddSingleton(s);
                       serviceCollection = serviceCollection.AddSingleton(s);
                   });

                

                await Commands.AddModulesAsync(assembly, innerServiceCollection.BuildServiceProvider());
            }
            Client.Ready += OnReady;
            Services = serviceCollection.BuildServiceProvider();

            await Client.LoginAsync(TokenType.Bot, token);
            await Client.StartAsync();

            Logger.LogInformation("Client started.... are we online?");
        }
        public async Task RemoveAllCommandsAsync()
        {
            foreach (var command in Commands.Commands.ToList())
            {
                try
                {
                    await Commands.RemoveModuleAsync(command.GetType());
                }
                catch (Exception ex)
                {
                    //suppress, command may not exist
                    Logger.LogError(ex.ToJsonString());
                }
            }
        }

        public async Task DisconnectAsync()
        {
            Logger.LogInformation("DisconnectAsync");


            try
            {
                await RemoveAllCommandsAsync();
                Client.MessageReceived -= OnMessageReceived;
            }
            catch (Exception ex)
            {

                Logger.LogWarning("Couldn't unregister events: " + ex.ConcatMessages());
            }

            try
            {
                await Client.StopAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Couldn't stop the client: " + ex.ConcatMessages());
            }

            try
            {
                await Client.LogoutAsync();
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Couldn't disconnect: " + ex.ConcatMessages());
            }
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            _waitHandle?.Set();
            PendingDisconnect = true;
            _timer?.Dispose();
            _timer = null;
        }



        private void StartTimer(int timeToRun)
        {
            _timer = new Timer(timeToRun);
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }
        private async Task OnMessageReceived(SocketMessage arg)
        {
            var message = arg as SocketUserMessage;

            if (message is null || message.Author.IsBot) return;

            int argPos = 0;
            if (!message.Content.Contains("!hash"))
            {
                Logger.LogInformation($"Message: {message.Author}: {message.Content}");
            }
            if (message.HasCharPrefix('!', ref argPos) || message.HasMentionPrefix(Client.CurrentUser, ref argPos))
            {
                var context = new SocketCommandContext(Client, message);
                var result = await Commands.ExecuteAsync(context, argPos, Services);

                if (!result.IsSuccess)
                {
                    Logger.LogInformation("Error executing command: " + result.ErrorReason + result.Error);
                }

            }
        }
    }

}
