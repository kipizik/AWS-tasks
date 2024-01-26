using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Aws.Common.Clients;
using FluentAssertions;
using System.Reflection;

namespace AWs.S3.Task5.Tests;

[TestFixture]
internal class S3FunctionalTests
{
    private ImageClient imageApiClient;
    private AmazonS3Client s3Client;
    private readonly List<int> uploadedImageIds = new();
    private readonly string imageDirectory = $"{Assembly.GetExecutingAssembly().Location}\\..\\Images";
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

        imageApiClient = new ImageClient(instance.PublicDnsName);
        s3Client = new AmazonS3Client();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (uploadedImageIds.Any())
        {
            foreach (var imageId in uploadedImageIds)
            {
                await imageApiClient.DeleteImageAsync(imageId);
            }
        }
    }

    [Test]
    public async Task Upload_An_Image()
    {
        const string fileName = "arsenal_fc.jpg";
        // Act
        var imageId = await imageApiClient.UploadImageAsync(fileName);
        imageId.Should().BeGreaterThan(0);

        uploadedImageIds.Add(imageId);

        // Assert
        // verify that image is stored in S3 bucket
        string s3BucketName = await GetFullBucketName();
        var s3Objects = await GetS3ObjectsAsync(s3BucketName);
        s3Objects.Should().ContainSingle(o => o.Key.Contains(fileName), $"File {fileName} should be available in S3 bucket {s3BucketName}");
    }

    [Test]
    public async Task View_List_Of_Uploaded_Images()
    {
        var expectedImages = Directory.EnumerateFiles(imageDirectory);
        var fileNames = expectedImages.Select(image => Path.GetFileName(image));
        foreach (var fileName in fileNames)
        {
            var id = await imageApiClient.UploadImageAsync(fileName);
            uploadedImageIds.Add(id);
        }

        var imagesUploaded = await imageApiClient.GetAllImagesMetadataAsync();

        imagesUploaded.Should().HaveCount(expectedImages.Count());
        foreach (var fileName in fileNames)
        {
            imagesUploaded.Select(x => GetImageName(x.ObjectKey)).Should().Contain(x => x.Contains(fileName));
        }
    }

    [Test]
    public async Task Delete_An_Image()
    {
        var expectedImage = Directory.EnumerateFiles(imageDirectory).First();
        var fileName = Path.GetFileName(expectedImage);
        var imageId = await imageApiClient.UploadImageAsync(fileName);

        var responseContent = await imageApiClient.DeleteImageAsync(imageId);
        responseContent.Trim().Should().Be("\"Image is deleted\"");

        // Assert
        // verify that image is not stored in S3 bucket
        string s3BucketName = await GetFullBucketName();
        var s3Objects = await GetS3ObjectsAsync(s3BucketName);
        s3Objects.Should().NotContain(o => o.Key.Contains(fileName), $"File {fileName} should not be available in S3 bucket {s3BucketName}");
    }

    [Test]
    public async Task Download_An_Image()
    {
        var expectedImagePath = Directory.EnumerateFiles(imageDirectory).First();
        var fileName = Path.GetFileName(expectedImagePath);
        var imageId = await imageApiClient.UploadImageAsync(fileName);
        uploadedImageIds.Add(imageId);

        var downloadedImage = await imageApiClient.DownloadImageAsync(imageId);
        var expectedImage = File.ReadAllBytes(expectedImagePath);

        // Assert
        downloadedImage.Should().BeEquivalentTo(expectedImage);
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
