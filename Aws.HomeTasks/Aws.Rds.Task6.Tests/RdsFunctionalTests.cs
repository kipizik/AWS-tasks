using Amazon.EC2;
using Amazon.EC2.Model;
using Aws.Common.Models;
using Aws.Common.Models.API;
using Aws.Rds.Task6.Tests.Helpers;
using FluentAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;

namespace Aws.Rds.Task6.Tests;

[TestFixture]
internal class RdsFunctionalTests
{
    private Instance ec2Instance;
    private string apiBaseAddress;
    private HttpClient imageApiClient;
    private readonly List<int> uploadedImageIds = new();
    private readonly string imageDirectory = $"{Assembly.GetExecutingAssembly().Location}\\..\\Images";

    [SetUp]
    public async Task SetUp()
    {
        var ec2Client = new AmazonEC2Client();
        var describeInstancesResponse = await ec2Client.DescribeInstancesAsync(
            new DescribeInstancesRequest
            {
                Filters = new List<Filter>
                {
                    new()
                    {
                        Name = "tag-key",
                        Values = new List<string> { "cloudx" }
                    }
                }
            });
        ec2Instance = describeInstancesResponse.Reservations.Single().Instances.Single();
        apiBaseAddress = $"http://{ec2Instance.PublicDnsName}/api";

        imageApiClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseAddress)
        };
        imageApiClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (uploadedImageIds.Any())
        {
            foreach (var imageId in uploadedImageIds)
            {
                await imageApiClient.DeleteAsync($"{apiBaseAddress}/image/{imageId}");
            }
        }
    }

    [Test]
    public async Task Uploaded_Image_Metadata_Should_Be_Stored_In_Db()
    {
        const string fileName = "arsenal_fc.jpg";
        // Act
        var imageId = await UploadFileAsync(fileName);
        imageId.Should().BeGreaterThan(0);

        uploadedImageIds.Add(imageId);

        var getImageMetadataResponse = await imageApiClient.GetAsync($"{apiBaseAddress}/image/{imageId}");
        var responseString = await getImageMetadataResponse.Content.ReadAsStringAsync();
        var uploadedImage = JsonConvert.DeserializeObject<ImageModel>(responseString);

        // Assert
        // verify that image stored in database is the same as returned by API
        var dbImages = await GetDataFromDb();
        var dbImageData = dbImages.FirstOrDefault(x => x.Any(kvp => kvp.Key == "id" && kvp.Value.ToString() == imageId.ToString()));
        dbImageData.Should().NotBeNull();
        ImageModel dbImage = MapToImageModel(dbImageData);
        uploadedImage.Should().BeEquivalentTo(dbImage);
    }

    [Test]
    public async Task Deleted_Image_Should_Be_Deleted_From_Db()
    {
        var expectedImage = Directory.EnumerateFiles(imageDirectory).First();
        var fileName = Path.GetFileName(expectedImage);
        var imageId = await UploadFileAsync(fileName);

        var deleteImageResponse = await imageApiClient.DeleteAsync($"{apiBaseAddress}/image/{imageId}");
        deleteImageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseString = await deleteImageResponse.Content.ReadAsStringAsync();
        responseString.Trim().Should().Be("\"Image is deleted\"");

        // Assert
        // verify that image is not stored in Db
        var dbImages = await GetDataFromDb();
        var dbImageData = dbImages.FirstOrDefault(x => x.Any(kvp => kvp.Key == "id" && kvp.Value.ToString() == imageId.ToString()));
        dbImageData.Should().BeNull("Image should not be present in DB after deletion.");
    }

    private async Task<int> UploadFileAsync(string fileName)
    {
        using var multipartFormDataContent = new MultipartFormDataContent();
        var imagePath = $"{imageDirectory}\\{fileName}";
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException();
        }

        byte[] imageBytes = File.ReadAllBytes(imagePath);
        multipartFormDataContent.Add(new ByteArrayContent(imageBytes), "upfile", fileName);
        var response = await imageApiClient.PostAsync($"{apiBaseAddress}/image", multipartFormDataContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseString = await response.Content.ReadAsStringAsync();
        var responseData = JsonConvert.DeserializeAnonymousType(responseString, new { Id = 0 });

        return responseData!.Id;
    }

    private async Task<List<IDictionary<string, object>>> GetDataFromDb()
    {
        var secretValues = await AwsSsmHelper.GetSecretAsync("DatabaseDBSecret");
        var dbConnectionInfo = new DbConnectionInfo
        {
            DbName = secretValues["dbname"],
            Password = secretValues["password"],
            UserName = secretValues["username"],
            Port = uint.Parse(secretValues["port"]),
            Server = secretValues["host"]
        };
        return await DbHelper.GetDataFromDbAsync(ec2Instance.PublicDnsName, "ec2-user", dbConnectionInfo, "images");
    }

    private static ImageModel MapToImageModel(IDictionary<string, object>? dbImageData)
    {
        var lastModified = DateTime.Parse(dbImageData["last_modified"].ToString()).ToString("yyyy-MM-ddTHH:mm:ssZ");
        return new ImageModel
        {
            Id = long.Parse(dbImageData!["id"].ToString()!),
            LastModified = lastModified,
            ObjectKey = dbImageData["object_key"].ToString()!,
            ObjectSize = long.Parse(dbImageData["object_size"].ToString()!),
            ObjectType = dbImageData["object_type"].ToString()!
        };
    }
}
