using Amazon.EC2;
using Amazon.EC2.Model;
using Aws.Common.Clients;
using Aws.Common.Helpers;
using Aws.Common.Models;
using Aws.Common.Models.API;
using FluentAssertions;
using NUnit.Framework;
using System.Reflection;

namespace Aws.Task6.Rds.Tests;

[TestFixture]
internal class RdsFunctionalTests
{
    private Instance ec2Instance;
    private ImageClient imageApiClient;
    private readonly List<string> uploadedImageIds = new();
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

        imageApiClient = new ImageClient(ec2Instance.PublicDnsName);
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
    public async Task Uploaded_Image_Metadata_Should_Be_Stored_In_Db()
    {
        const string fileName = "arsenal_fc.jpg";
        // Act
        var imageId = await imageApiClient.UploadImageAsync(fileName);
        imageId.Should().NotBeNullOrEmpty();

        uploadedImageIds.Add(imageId);

        var uploadedImage = await imageApiClient.GetImageMetadataAsync(imageId);
        uploadedImage.ObjectSize = uploadedImage.ObjectSize.Replace(".0", string.Empty);
        uploadedImage.CreatedAt = uploadedImage.CreatedAt.Replace(".0", string.Empty);

        // Assert
        // verify that image stored in database is the same as returned by API
        var dbImages = await GetDataFromDb(DbType.NoSQL);
        var dbImageData = dbImages.FirstOrDefault(x => x.Any(kvp => kvp.Key == "id" && kvp.Value == imageId));
        dbImageData.Should().NotBeNull();
        ImageModel dbImage = MapToImageModel(dbImageData);
        uploadedImage.Should().BeEquivalentTo(dbImage, opt => opt.Excluding(x => x.LastModified));
    }

    [Test]
    public async Task Deleted_Image_Should_Be_Deleted_From_Db()
    {
        var expectedImage = Directory.EnumerateFiles(imageDirectory).First();
        var fileName = Path.GetFileName(expectedImage);
        var imageId = await imageApiClient.UploadImageAsync(fileName);

        var deleteImageResponse = await imageApiClient.DeleteImageAsync(imageId);
        deleteImageResponse.Trim().Should().Be("\"Image is deleted\"");

        // Assert
        // verify that image is not stored in Db
        var dbImages = await GetDataFromDb(DbType.NoSQL);
        var dbImageData = dbImages.FirstOrDefault(x => x.Any(kvp => kvp.Key == "id" && kvp.Value == imageId));
        dbImageData.Should().BeNull("Image should not be present in DB after deletion.");
    }

    private async Task<List<Dictionary<string, string>>> GetDataFromDb(DbType dbType)
    {
        return dbType switch
        {
            DbType.SQL => await DbHelper.GetDataFromSqlDbAsync(ec2Instance.PublicDnsName),
            DbType.NoSQL => await DbHelper.GetDataFromNoSqlDbAsync(),
            _ => throw new Exception("DB type is not supported"),
        };
    }

    private static ImageModel MapToImageModel(Dictionary<string, string>? dbImageData)
    {
        return new ImageModel
        {
            Id = dbImageData!["id"],
            LastModified = dbImageData["last_modified"],
            ObjectKey = dbImageData["object_key"],
            ObjectSize = dbImageData["object_size"],
            ObjectType = dbImageData["object_type"],
            CreatedAt = dbImageData["created_at"]
        };
    }
}
