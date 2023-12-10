using System.Threading.Tasks;
using Discord;

public interface IS3Service
{
    Task<FileAttachment?> DownloadFileAsync(string url);
}