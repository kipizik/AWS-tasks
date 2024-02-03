using Amazon.EC2;
using Amazon.EC2.Model;
using Aws.Common.Helpers;
using FluentAssertions;
using FluentAssertions.Execution;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Aws.Task3.Ec2.Tests;

public class Tests
{
    private AmazonEC2Client ec2Client;

    [SetUp]
    public void Setup()
    {
        ec2Client = new AmazonEC2Client(CredentialsManager.GetAccessKeyId(), CredentialsManager.GetSecretAccessKey());
    }

    [Test]
    public async Task Validate_EC2_Configuration()
    {
        var expectedVolumeSize = 8;
        var expectedInstanceType = InstanceType.T2Micro.Value;
        var expectedTags = new List<string> { "Name", "cloudx" };

        var describeInstancesRequest = new DescribeInstancesRequest
        {
            Filters = new List<Filter>
            {
                new()
                {
                    Name = "instance-type",
                    Values = new List<string> { expectedInstanceType }
                }
            }
        };
        var describeInstanceTypesResponse = await ec2Client.DescribeInstancesAsync(describeInstancesRequest);
        IEnumerable<Instance> ec2Instances = describeInstanceTypesResponse.Reservations.SelectMany(r => r.Instances);

        // Assert
        using (new AssertionScope())
        {
            ec2Instances.Count().Should().Be(2, $"2 EC2 instances of type {expectedInstanceType} should be deployed.");

            foreach (var instance in ec2Instances)
            {
                var describeVolumesRequest = new DescribeVolumesRequest
                {
                    VolumeIds = new List<string> { instance.BlockDeviceMappings[0].Ebs.VolumeId }
                };

                var describeVolumesResponse = await ec2Client.DescribeVolumesAsync(describeVolumesRequest);

                // Assert
                describeVolumesResponse.Volumes.Should().ContainSingle().Which.Size.Should().Be(expectedVolumeSize);
                instance.Tags.Select(t => t.Key).Should().Contain(expectedTags);
                instance.InstanceType.Should().Be(expectedInstanceType);

                if (instance.IamInstanceProfile.Arn.Contains("Public", StringComparison.OrdinalIgnoreCase))
                {
                    instance.PublicIpAddress.Should().NotBeNullOrEmpty();
                }
                else
                {
                    instance.PublicIpAddress.Should().BeNullOrEmpty();
                }
            }
        }
    }

    [Test]
    public async Task Validate_Security_Groups()
    {
        var sshPort = 22;
        var httpPort = 80;

        var describeSecurityGroupsRequest = new DescribeSecurityGroupsRequest();
        var describeSecurityGroupsResponse = await ec2Client.DescribeSecurityGroupsAsync(describeSecurityGroupsRequest);
        var deployedSecurityGroups = describeSecurityGroupsResponse.SecurityGroups.Where(sg => sg.GroupName.StartsWith("cloudxinfo"));
        // Assert
        deployedSecurityGroups.Should().HaveCount(2);

        // validate Security Groups information
        // public group
        var publicSecurityGroup = deployedSecurityGroups.Single(sg => sg.GroupName.Contains("Public", StringComparison.OrdinalIgnoreCase));
        // Assert
        publicSecurityGroup.IpPermissions.Should().HaveCount(2, "2 Ip permissions should be available in Public security group");
        publicSecurityGroup.IpPermissions.Should().Contain(permission =>
             permission.FromPort == httpPort
             && permission.ToPort == httpPort
             && permission.Ipv4Ranges.Any(r => r.CidrIp == "0.0.0.0/0" && r.Description == "HTTP from Internet"));
        publicSecurityGroup.IpPermissions.Should().Contain(permission =>
             permission.FromPort == sshPort
             && permission.ToPort == sshPort
             && permission.Ipv4Ranges.Any(r => r.CidrIp == "0.0.0.0/0" && r.Description == "SSH from Internet"));

        // private group
        var privateSecurityGroup = deployedSecurityGroups.Single(sg => sg.GroupName.Contains("Private", StringComparison.OrdinalIgnoreCase));
        // Assert
        privateSecurityGroup.IpPermissions.Should().HaveCount(2, "2 Ip permissions should be available in Public security group");
        privateSecurityGroup.IpPermissions.Should().Contain(permission =>
             permission.FromPort == httpPort
             && permission.ToPort == httpPort
             && !permission.Ipv4Ranges.Any());
        privateSecurityGroup.IpPermissions.Should().Contain(permission =>
             permission.FromPort == sshPort
             && permission.ToPort == sshPort
             && !permission.Ipv4Ranges.Any());
        // verify that private instance can be accessible from public instance
        IEnumerable<string> groupIds = privateSecurityGroup.IpPermissions.SelectMany(p => p.UserIdGroupPairs.Select(gp => gp.GroupId));
        groupIds.Distinct().Should().ContainSingle(userId => userId == publicSecurityGroup.GroupId, "Only public instaces should have access.");
    }

    [Test]
    public async Task Validate_EC2_Endpoint()
    {
        // Arrange
        // get info through AWS SDK
        var describeInstancesRequest = new DescribeInstancesRequest();
        var describeInstancesResponse = await ec2Client.DescribeInstancesAsync(describeInstancesRequest);
        var publicInstance = describeInstancesResponse.Reservations.
            SelectMany(r => r.Instances)
            .Single(i => !string.IsNullOrEmpty(i.PublicIpAddress));
        var expectedInstanceMetadata = new
        {
            availability_zone = publicInstance.Placement.AvailabilityZone,
            private_ipv4 = publicInstance.PrivateIpAddress,
            region = ec2Client.Config.RegionEndpoint.SystemName
        };

        // Act
        // get info from deployed API
        var client = new HttpClient();
        var response = await client.GetAsync($"http://{publicInstance.PublicIpAddress}");
        var responseContent = await response.Content.ReadAsStringAsync();
        var instanceInfoReturned = JsonConvert.DeserializeAnonymousType(responseContent, expectedInstanceMetadata);

        instanceInfoReturned.Should().BeEquivalentTo(expectedInstanceMetadata);
    }
}
