using System.Composition;
using System.Threading.Tasks;
using System.Threading;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System;
using Microsoft.Extensions.Logging;
using AWS.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using DiscordBot.Core.Contract;

namespace DiscordBot.Core
{

    public class DiscordBotLocal : LoggingResource
    {
        [Import]
        private IConnectionService _connectionService { get; set; } 

        public DiscordBotLocal() : base(nameof(DiscordBotLocal)) { }

        public async Task RunAsync()
        {
            Logger.LogInformation("Disconnecting in case we didn't get a chance to before.");
            await _connectionService.DisconnectAsync();

            try
            {
                var waitHandle = new AutoResetEvent(false);
                var token = await GetToken() ?? throw new NullReferenceException("Token was null... uh oh!");
                Logger.LogInformation($"Connecting....");
                await _connectionService.InitializeAsync(Client_Ready, token, 0, waitHandle);

                waitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Unhandled error: {ex.Message}. Stack Trace: {ex.StackTrace}");
                Logger.LogError($"Discord bot exited unexpectedly!");
                await _connectionService.DisconnectAsync();
            }

        }

        public async Task Client_Ready() => await _connectionService.Client.SetGameAsync("whimsically with electrons...", type: Discord.ActivityType.Playing);


        public async Task<string?> GetToken()
        {
            string secretName = Environment.GetEnvironmentVariable("TOKENNAME");
            string region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");

            using var client = new AmazonSecretsManagerClient(region: Amazon.RegionEndpoint.GetBySystemName(region));

            GetSecretValueRequest request = new GetSecretValueRequest
            {
                SecretId = secretName
            };

            GetSecretValueResponse response;
            try
            {
                response = await client.GetSecretValueAsync(request);
                if (response.SecretString != null)
                {
                    var parsedSecrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.SecretString);
                    if (parsedSecrets != null && parsedSecrets.ContainsKey("Token"))
                    {
                        return parsedSecrets["Token"];
                    }
                }
                else
                {
                    Logger.LogError("The secret string was empty... invalid!");
                }
            }
            catch (ResourceNotFoundException)
            {
                Logger.LogError("The requested secret " + secretName + " was not found");
            }
            catch (InvalidRequestException e)
            {
                Logger.LogError("The request was invalid due to: " + e.Message);
            }
            catch (InvalidParameterException e)
            {
                Logger.LogError("The request had invalid params: " + e.Message);
            }

            return null;
        }

    }

}
