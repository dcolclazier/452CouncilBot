using System;
using System.Composition;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Discord;

[Export(typeof(IS3Service))]
[Shared]
public class S3Service : IS3Service
{
    private readonly AmazonS3Client _s3Client = new();

    public async Task<FileAttachment?> DownloadFileAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var bucketName = uri.Host.Split('.')[0];
            var key = uri.AbsolutePath[1..]; // Remove the leading '/'

            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            using var response = await _s3Client.GetObjectAsync(request);
            var stream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(stream);
            stream.Position = 0;
            var fileName = Path.GetFileName(key);
            return new FileAttachment(stream, fileName);
        }
        catch
        {
            return null;
        }
    }
}