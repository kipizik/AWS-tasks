using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Aws.Common.Extensions;
using Aws.Common.Models;
using FluentAssertions;
using FluentAssertions.Execution;
using NUnit.Framework;
using System.Data;
using System.Net;

namespace AWs.Task5.S3.Tests;

public class S3DeploymentValidationTests
{
    private AmazonEC2Client ec2Client;
    private AmazonS3Client s3Client;
    private AmazonIdentityManagementServiceClient iamClient;

    [SetUp]
    public void Setup()
    {
        ec2Client = new AmazonEC2Client();
        s3Client = new AmazonS3Client();
        iamClient = new AmazonIdentityManagementServiceClient();
    }

    [Test]
    public async Task Instance_Is_Reachable_Over_HTTP()
    {
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

        using (var httpClient = new HttpClient())
        {
            var response = await httpClient.GetAsync($"http://{instance.PublicIpAddress}/api/ui");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task AppInstance_Can_Access_S3Bucket()
    {
        var listBucketsResponse = await s3Client.ListBucketsAsync();
        var cloudxBuckets = listBucketsResponse.Buckets.Where(b => b.BucketName.Contains("cloudx"));

        cloudxBuckets.Should().NotBeEmpty();

        var s3BucketArns = cloudxBuckets.Select(b => $"arn:aws:s3:::{b.BucketName}");
        InstanceProfile instanceProfile = await GetEc2InstanceProfileByTag("cloudx");
        List<PolicyVersionModel> policyDocuments = await iamClient.GetPolicyDocumentsByIamRoleAsync(instanceProfile.Roles.Single().RoleName);
        var s3AccessPolicies = policyDocuments.Where(d =>
            d.Statement.Any(s =>
                s.Effect == "Allow"
                && s3BucketArns.Contains(s.Resource)
                && s.Action.ToString()!.Contains("s3:ListBucket")));

        s3AccessPolicies.Should().NotBeEmpty();
    }

    [Test]
    public async Task Validate_S3Bucket_Requirements()
    {
        var requiredBucketNamePart = "cloudximage-imagestorebucket";
        var requiredTagKey = "cloudx";

        // Check the bucket exist
        var listBucketsResponse = await s3Client.ListBucketsAsync();
        var matchedBuckets = listBucketsResponse.Buckets.Where(b => b.BucketName.Contains(requiredBucketNamePart));
        matchedBuckets.Should().ContainSingle();

        string bucketName = matchedBuckets.Single().BucketName;
        var getBucketTaggingResponse = await s3Client.GetBucketTaggingAsync(new GetBucketTaggingRequest
        {
            BucketName = bucketName,
        });
        var getBucketEncryptionResponse = await s3Client.GetBucketEncryptionAsync(new GetBucketEncryptionRequest
        {
            BucketName = bucketName
        });
        var getBucketVersioningResponse = await s3Client.GetBucketVersioningAsync(new GetBucketVersioningRequest
        {
            BucketName = bucketName
        });
        var getPublicAccessBlockResponse = await s3Client.GetPublicAccessBlockAsync(new GetPublicAccessBlockRequest
        {
            BucketName = bucketName
        });

        using (new AssertionScope())
        {
            // Check tag
            getBucketTaggingResponse.TagSet.Should().Contain(t => t.Key == requiredTagKey);
            // Check encryption
            getBucketEncryptionResponse.ServerSideEncryptionConfiguration.ServerSideEncryptionRules.Should()
                .ContainSingle(r => r.ServerSideEncryptionByDefault.ServerSideEncryptionAlgorithm == ServerSideEncryptionMethod.AES256);
            // Check versioning
            getBucketVersioningResponse.VersioningConfig.Status.Should().Be(VersionStatus.Off);
            // Check public access is disabled
            getPublicAccessBlockResponse.PublicAccessBlockConfiguration.RestrictPublicBuckets.Should().BeTrue();
            getPublicAccessBlockResponse.PublicAccessBlockConfiguration.IgnorePublicAcls.Should().BeTrue();
        }
    }

    private async Task<InstanceProfile> GetEc2InstanceProfileByTag(string tagName)
    {
        var instanceProfileName = await ec2Client.GetEc2InstanceProfileNameByTag(tagName);
        var instanceProfilesResponse = await iamClient.GetInstanceProfileAsync(
            new GetInstanceProfileRequest
            {
                InstanceProfileName = instanceProfileName
            });
        return instanceProfilesResponse.InstanceProfile;
    }
}