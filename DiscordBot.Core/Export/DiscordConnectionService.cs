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
using Discord.Interactions;

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
    [AttributeUsage(AttributeTargets.Method)]
    public class DiscordInteractionAttribute : Attribute
    {
        public string CustomId { get; }

        public DiscordInteractionAttribute(string customId)
        {
            CustomId = customId;
        }
    }

    [Export(typeof(IConnectionService))]
    [Shared]
    public class DiscordConnectionService : LoggingResource, IConnectionService
    {
        public DiscordSocketClient Client { get; private set; }
        public CommandService Commands { get; private set; }
        public ServiceProvider Services { get; private set; }
        public InteractionService InteractionService { get; private set; }

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
            Commands = new CommandService(new CommandServiceConfig
            {
                CaseSensitiveCommands = true,
                DefaultRunMode = Discord.Commands.RunMode.Async,
                LogLevel = LogSeverity.Debug,
                IgnoreExtraArgs = true
            });

            InteractionService = new InteractionService(Client, new InteractionServiceConfig()
            {
                DefaultRunMode = Discord.Interactions.RunMode.Async,
                LogLevel = LogSeverity.Debug,
                InteractionCustomIdDelimiters = new []{'|'},
                
            });

        }

        public async Task InitializeAsync(Func<Task> onReady, string token, int timeToRun, EventWaitHandle waitHandle)
        {
            _waitHandle = waitHandle;
            Logger.LogInformation("InitializeAsync");
            if (timeToRun > 0) StartTimer(timeToRun);

            await RemoveAllCommandsAsync();

            var serviceCollection = new ServiceCollection().AddSingleton(Client).AddSingleton(Commands);
            foreach (var assembly in _assemblyFactory.Assemblies().ToList().Select(Assembly.LoadFile))
            {
                await RegisterCommandsFromAssemblyAsync(assembly, serviceCollection);
                RegisterEventHandlersFromAssembly(assembly);
                await RegisterInteractionHandlers(assembly);
            }

            Client.MessageReceived += OnMessageReceived;
            Client.InteractionCreated += OnInteractionCreated;
            Client.Ready += onReady;
            Services = serviceCollection.BuildServiceProvider();

            await Client.LoginAsync(TokenType.Bot, token);
            await Client.StartAsync();

            Logger.LogInformation("Client started.... are we online?");
        }
        public async Task RegisterCommandsFromAssemblyAsync(Assembly assembly, IServiceCollection serviceCollection)
        {
            var innerServiceCollection = new ServiceCollection().AddSingleton(Client).AddSingleton(Commands);
            assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<DiscordCommandAttribute>(true) != null ||
                            t.GetCustomAttribute<ExportAttribute>(true) != null)
                .ToList()
                .ForEach(s =>
                {
                    Logger.LogInformation($"Found Command Class: {s.Name}");
                    innerServiceCollection.AddSingleton(s);
                    serviceCollection.AddSingleton(s);
                });

            await Commands.AddModulesAsync(assembly, innerServiceCollection.BuildServiceProvider());
        }
        private async Task RegisterInteractionHandlers(Assembly assembly)
        {
            // Find all classes that extend InteractionModuleBase
            var interactionModules = assembly.GetTypes()
                .Where(x => x.IsSubclassOf(typeof(InteractionModuleBase<SocketInteractionContext>)))
                .ToList();

            // Register each module with the InteractionService
            foreach (var module in interactionModules)
            {
                await InteractionService.AddModuleAsync(module, Services);
                Logger.LogInformation($"Registered interaction module: {module.Name}");
            }
        }
        private async Task OnInteractionCreated(SocketInteraction interaction)
        {
            try
            {
                Logger.LogInformation("Interaction created!");
                var ctx = new SocketInteractionContext(Client, interaction);
                await InteractionService.ExecuteCommandAsync(ctx, Services);
                Logger.LogInformation("Executed command.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to handle interaction: {ex}");
            }
        }
        public void RegisterEventHandlersFromAssembly(Assembly assembly)
        {
            assembly.GetTypes()
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
                        var handlerInstance = Activator.CreateInstance(method.DeclaringType);
                        var delegateInstance = Delegate.CreateDelegate(eventInfo.EventHandlerType, handlerInstance, method.Name);
                        eventInfo.AddEventHandler(Client, delegateInstance);
                        Logger.LogInformation($"Subscribed {method.Name} to DiscordSocketClient.{eventInfo.Name}");
                    }
                    else
                    {
                        Logger.LogWarning($"Could not find event '{attribute.EventName}' in DiscordSocketClient.");
                    }
                });
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
