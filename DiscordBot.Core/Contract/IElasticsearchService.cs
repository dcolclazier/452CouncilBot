using Nest;
using System.Threading.Tasks;
namespace DiscordBot.Core.Contract
{
    //public interface IEvidenceStorageService
    //{
    //    Task<List<string>> UploadEvidenceAsync(IEnumerable<string> evidenceUrls);
    //}

    public interface IElasticsearchService
    {
        Task<PlayerRecord> GetPlayerByIdAsync(string playerId);
        Task<string> CreateOrUpdatePlayerAsync(PlayerRecord player);
        Task<OffenseReport> GetOffenseReportByIdAsync(string reportId);
        Task<string> CreateOffenseReportAsync(OffenseReport report);

        Task<ElasticClient> InitializeElasticsearchClientAsync(string defaultIndex);
    }
}