using Aws.Common.Models.API;
using Newtonsoft.Json;
using System.Reflection;

namespace Aws.Common.Clients;

public class ImageClient
{
    private readonly HttpClient httpClient;
    public readonly string ImageDirectory = $"{Assembly.GetExecutingAssembly().Location}\\..\\Images";

    public ImageClient(string hostName)
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{hostName}/api/")
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<byte[]> DownloadImageAsync(string imageId)
    {
        return await httpClient.GetByteArrayAsync($"image/file/{imageId}");
    }

    public async Task<ImageModel[]> GetAllImagesMetadataAsync()
    {
        var responseBody = await httpClient.GetStringAsync("image");
        var imagesMetadata = JsonConvert.DeserializeObject<ImageModel[]>(responseBody);

        return imagesMetadata!;
    }
    public async Task<ImageModel> GetImageMetadataAsync(string imageId)
    {
        var responseBody = await httpClient.GetStringAsync($"image/{imageId}");
        var imageMetadata = JsonConvert.DeserializeObject<ImageModel>(responseBody);

        return imageMetadata!;
    }

    public async Task<string> UploadImageAsync(string fileName)
    {
        using var multipartFormDataContent = new MultipartFormDataContent();
        var imagePath = $"{ImageDirectory}\\{fileName}";
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException();
        }

        byte[] imageBytes = File.ReadAllBytes(imagePath);
        multipartFormDataContent.Add(new ByteArrayContent(imageBytes), "upfile", fileName);
        var response = await httpClient.PostAsync("image", multipartFormDataContent);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var responseData = JsonConvert.DeserializeAnonymousType(responseString, new { Id = string.Empty });

        return responseData!.Id;
    }

    public async Task<string> DeleteImageAsync(string imageId)
    {
        var response = await httpClient.DeleteAsync($"image/{imageId}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
}
