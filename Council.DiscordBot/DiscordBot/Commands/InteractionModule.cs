using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using DiscordBot.Core.Contract;
using MEF.NetCore;

public class InteractionModule : InteractionModuleBase<SocketInteractionContext>
{
    [Import] private IElasticsearchService ElasticClient { get; set; }
    [Import] private IS3Service S3Service { get; set; }
    public InteractionModule()
    {
        MEFLoader.SatisfyImportsOnce(this);
    }
    [ComponentInteraction("report_*")]
    public async Task GetOfficeReportButtonClicked(string reportId)
    {
        try
        {
            // Fetch offense report
            var offenseReport = await ElasticClient.GetOffenseReportByIdAsync(reportId);

            // Download evidence files and prepare attachments
            var attachments = new List<FileAttachment>();
            foreach (var url in offenseReport.evidenceUrls)
            {
                var file = await S3Service.DownloadFileAsync(url);
                if (file != null)
                {
                    attachments.Add((FileAttachment)file);
                }
            }

            await Context.Channel.SendFilesAsync(attachments);
            await RespondAsync(embed: offenseReport.Embed().Build());
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message + ex.StackTrace);
            await RespondAsync($"Gross... I swallowed a bug: {ex.Message} {ex.StackTrace}");
        }
        
    }
}