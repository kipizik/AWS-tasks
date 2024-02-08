using Amazon;
using Amazon.CloudTrail;
using Amazon.CloudTrail.Model;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using FluentAssertions;
using FluentAssertions.Execution;
using NUnit.Framework;

namespace Aws.Task9.CloudWatch.Tests;

public class DeploymentValidationTests
{
    private AmazonEC2Client _ec2Client;
    private AmazonCloudWatchClient _cloudWatchClient;
    private AmazonCloudTrailClient _cloudTrailClient;
    private AmazonCloudWatchLogsClient _cloudWatchLogsClient;

    [OneTimeSetUp]
    public void Setup()
    {
        _ec2Client = new AmazonEC2Client();
        _cloudWatchClient = new AmazonCloudWatchClient();
        _cloudTrailClient = new AmazonCloudTrailClient();
        _cloudWatchLogsClient = new AmazonCloudWatchLogsClient();
    }

    [Test]
    public async Task Ec2_Instance_Has_CloudWatch_Integration()
    {
        var describeInstancesResponse = await _ec2Client.DescribeInstancesAsync(
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
        var instance = describeInstancesResponse.Reservations.SelectMany(r => r.Instances).First(i => i.State.Name == InstanceStateName.Running);

        var request = new ListMetricsRequest();
        request.Dimensions.Add(new DimensionFilter { Name = "InstanceId", Value = instance.InstanceId });

        var response = await _cloudWatchClient.ListMetricsAsync(request);

        response.Metrics.Should().Contain(m => m.Dimensions.Any(d => d.Name == "InstanceId" && d.Value == instance.InstanceId),
            "CloudWatch metrics should be available for the instance if integration is properly configured");
    }

    [Test]
    public async Task Verify_CloudWatch_Requirements()
    {
        string lambdaLogGroupName = "/aws/lambda/cloudxserverless-EventHandlerLambda";
        string applicationLogGroupName = "/var/log/cloudxserverless-app";
        string cloudInitLogGroupName = "/var/log/cloud-init";

        // 2 log groups in the same region
        var serverlessLogGroupsRequest = new DescribeLogGroupsRequest
        {
            LogGroupNamePattern = "cloudxserverless",
        };
        var describeLogGroupsResponse = await _cloudWatchLogsClient.DescribeLogGroupsAsync(serverlessLogGroupsRequest);
        var logGroupNames = describeLogGroupsResponse.LogGroups.Select(lg => lg.LogGroupName);

        using (new AssertionScope())
        {
            logGroupNames.Should().Contain(name => name.Contains(lambdaLogGroupName));
            logGroupNames.Should().Contain(name => name.Contains(applicationLogGroupName));
        }

        // one log group in us-east1
        var cloudInitLogGroupsREquest = new DescribeLogGroupsRequest
        {
            LogGroupNamePattern = "cloud-init"
        };
        var usEastCloudWatchClient = new AmazonCloudWatchLogsClient(RegionEndpoint.USEast1);
        var usEastLogGroupsResponse = await usEastCloudWatchClient.DescribeLogGroupsAsync();
        var usEastLogGroupNames = usEastLogGroupsResponse.LogGroups.Select(lg => lg.LogGroupName);

        usEastLogGroupNames.Should().Contain(name => name.Contains(cloudInitLogGroupName));
    }

    [Test]
    public async Task Validate_CloudTrail_Requirements()
    {
        var expectedTrailName = "cloudxserverless-Trail";
        var expectedTag = "cloudx";
        var describeTrailsResponse = await _cloudTrailClient.DescribeTrailsAsync();
        var trail = describeTrailsResponse.TrailList.FirstOrDefault(trail => trail.Name.Contains(expectedTrailName));
        // Validate the trail name
        trail.Should().NotBeNull();

        var listTagsRequest = new ListTagsRequest { ResourceIdList = new List<string> { trail.TrailARN } };
        var listTagsResponse = await _cloudTrailClient.ListTagsAsync(listTagsRequest);

        using (new AssertionScope())
        {
            // Validate the multi-region property
            trail.IsMultiRegionTrail.Should().BeTrue();
            // Validate the log file validation
            trail.LogFileValidationEnabled.Should().BeTrue();
            // Validate that KMS encryption is not enabled
            trail.KmsKeyId.Should().BeNull();
            // Validate the tags
            listTagsResponse.ResourceTagList[0].TagsList.Should().Contain(t => t.Key == expectedTag); 
        }
    }
}