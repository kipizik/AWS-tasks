using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Aws.Common.Models;
using FluentAssertions;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;

namespace AWs.S3.Task5.Tests;

[TestFixture]
internal class FunctionalTests
{
    private string apiBaseAddress;
    private HttpClient imageApiClient;
    private AmazonS3Client s3Client;
    private readonly List<int> createdS3ObjectIds = new();
    private readonly string projectImageDirectory = $"{Assembly.GetExecutingAssembly().Location}\\..\\Images";
    private const string bucketNamePart = "cloudximage-imagestorebucket";

    /*
- Download images from the S3 bucket
*/

    [SetUp]
    public async Task SetUp()
    {
        var ec2Client = new AmazonEC2Client();
        var describeInstancesResponse = await ec2Client.DescribeInstancesAsync(
            new DescribeInstancesRequest
            {
                Filters = new List<Amazon.EC2.Model.Filter>
                {
                    new()
                    {
                        Name = "tag-key",
                        Values = new List<string> { "cloudx" }
                    }
                }
            });
        var instance = describeInstancesResponse.Reservations.Single().Instances.Single();
        apiBaseAddress = $"http://{instance.PublicDnsName}/api";

        imageApiClient = new HttpClient
        {
            BaseAddress = new Uri(apiBaseAddress)
        };
        imageApiClient.DefaultRequestHeaders.Add("Accept", "application/json");
        s3Client = new AmazonS3Client();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (createdS3ObjectIds.Any())
        {
            foreach (var s3ObjectId in createdS3ObjectIds)
            {
                await imageApiClient.DeleteAsync($"{apiBaseAddress}/image/{s3ObjectId}");
            }
        }
    }

    [Test]
    public async Task Upload_An_Image()
    {
        const string fileName = "arsenal_fc.jpg";
        // Act
        var imageId = await UploadFileAsync(fileName);
        imageId.Should().BeGreaterThan(0);

        createdS3ObjectIds.Add(imageId);

        // Assert
        // verify that image is stored in S3 bucket
        string s3BucketName = await GetFullBucketName();
        var s3Objects = await GetS3ObjectsAsync(s3BucketName);
        s3Objects.Should().ContainSingle(o => o.Key.Contains(fileName), $"File {fileName} should be available in S3 bucket {s3BucketName}");
    }

    [Test]
    public async Task View_List_Of_Uploaded_Images()
    {
        var expectedImages = Directory.EnumerateFiles(projectImageDirectory);
        var fileNames = expectedImages.Select(image => Path.GetFileName(image));
        foreach (var fileName in fileNames)
        {
            var id = await UploadFileAsync(fileName);
            createdS3ObjectIds.Add(id);
        }

        var getImagesResponse = await imageApiClient.GetAsync($"{apiBaseAddress}/image");
        getImagesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseString = await getImagesResponse.Content.ReadAsStringAsync();
        var imagesUploaded = JsonConvert.DeserializeObject<List<ImageModel>>(responseString);

        imagesUploaded.Should().HaveCount(expectedImages.Count());
        foreach (var fileName in fileNames)
        {
            imagesUploaded.Select(x => GetImageName(x.ObjectKey)).Should().Contain(x => x.Contains(fileName));
        }
    }

    [Test]
    public async Task Delete_An_Image()
    {
        var expectedImage = Directory.EnumerateFiles(projectImageDirectory).First();
        var fileName = Path.GetFileName(expectedImage);
        var imageId = await UploadFileAsync(fileName);

        var getImagesResponse = await imageApiClient.DeleteAsync($"{apiBaseAddress}/image/{imageId}");
        getImagesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseString = await getImagesResponse.Content.ReadAsStringAsync();
        responseString.Trim().Should().Be("\"Image is deleted\"");

        // Assert
        // verify that image is not stored in S3 bucket
        string s3BucketName = await GetFullBucketName();
        var s3Objects = await GetS3ObjectsAsync(s3BucketName);
        s3Objects.Should().NotContain(o => o.Key.Contains(fileName), $"File {fileName} should not be available in S3 bucket {s3BucketName}");
    }

    [Test]
    public async Task Download_An_Image()
    {
        var expectedImage = Directory.EnumerateFiles(projectImageDirectory).First();
        var fileName = Path.GetFileName(expectedImage);
        var imageId = await UploadFileAsync(fileName);
        createdS3ObjectIds.Add(imageId);

        var getImagesResponse = await imageApiClient.GetByteArrayAsync($"{apiBaseAddress}/image/file/{imageId}");
        using var ms = new MemoryStream(getImagesResponse);
        using var image = System.Drawing.Image.FromStream(ms);

        // Assert
        File.ReadAllBytes(expectedImage).Should().BeEquivalentTo(getImagesResponse);
    }

    private async Task<string> GetFullBucketName()
    {
        var listBucketsResponse = await s3Client.ListBucketsAsync();
        var s3Bucket = listBucketsResponse.Buckets.Single(b => b.BucketName.Contains(bucketNamePart));
        return s3Bucket.BucketName;
    }

    private string GetImageName(string s3KeyName)
    {
        return s3KeyName.Contains('-') ? s3KeyName.Split('-').Last() : s3KeyName;
    }

    private async Task<int> UploadFileAsync(string fileName)
    {
        using var multipartFormDataContent = new MultipartFormDataContent();
        var imagePath = $"{projectImageDirectory}\\{fileName}";
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

    private async Task<List<S3Object>> GetS3ObjectsAsync(string bucketName)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName
        };
        var response = await s3Client.ListObjectsV2Async(request);

        return response.S3Objects;
    }
}
