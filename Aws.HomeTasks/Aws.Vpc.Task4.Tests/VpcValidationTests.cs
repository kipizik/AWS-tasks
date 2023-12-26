using Amazon.EC2;
using Amazon.EC2.Model;
using Aws.Common.Helpers;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Aws.Vpc.Task4.Tests;

public class VpcValidationTests
{
    private AmazonEC2Client ec2Client;

    [SetUp]
    public void Setup()
    {
        ec2Client = new AmazonEC2Client(CredentialsManager.GetAccessKeyId(), CredentialsManager.GetSecretAccessKey());
    }

    [Test]
    public async Task Validate_Security_Groups()
    {
        var describeSecurityGroupsRequest = new DescribeSecurityGroupsRequest();
        var describeSecurityGroupsResponse = await ec2Client.DescribeSecurityGroupsAsync(describeSecurityGroupsRequest);
        var deployedSecurityGroups = describeSecurityGroupsResponse.SecurityGroups.Where(sg => sg.GroupName.StartsWith("cloudxinfo"));
        deployedSecurityGroups.Should().HaveCount(2);

        var publicSecurityGroup = deployedSecurityGroups.Single(sg => sg.GroupName.Contains("Public", StringComparison.OrdinalIgnoreCase));
        var privateSecurityGroup = deployedSecurityGroups.Single(sg => sg.GroupName.Contains("Private", StringComparison.OrdinalIgnoreCase));

        // verify that private instance can be accessible from public instance
        IEnumerable<string> groupIds = privateSecurityGroup.IpPermissions.SelectMany(p => p.UserIdGroupPairs.Select(gp => gp.GroupId));
        groupIds.Distinct().Should().ContainSingle(userId => userId == publicSecurityGroup.GroupId, "Only public instances should have access.");
    }

    [Test]
    public async Task Validate_VPC_Configuration()
    {
        var expectedCidr = "10.0.0.0/16";
        var expectedTag = "cloudx";
        var describeVpcsResponse = await ec2Client.DescribeVpcsAsync();
        var targetVpc = describeVpcsResponse.Vpcs.SingleOrDefault(vpc => vpc.CidrBlock == expectedCidr);
        // Assert
        targetVpc.Should().NotBeNull("VPC with specified CIDR block not found.");

        var describeTagsRequest = new DescribeTagsRequest
        {
            Filters = new List<Filter>
            {
                new() { Name = "resource-id", Values = new List<string> { targetVpc!.VpcId } },
                new() { Name = "key", Values = new List<string> { "Name", expectedTag } }
            }
        };
        var describeTagsResponse = await ec2Client.DescribeTagsAsync(describeTagsRequest);
        // Assert
        describeTagsResponse.Tags.Should().NotBeEmpty("VPC does not have mandatory tags");
    }

    [Test]
    public async Task Validate_Subnet_Configuration()
    {
        // Assuming the public subnet has a tag key of 'type' with value 'public' and private subnet has 'type' value as 'private',
        // and NAT gateway is associated with the subnet tagged as 'public' and has a tag key 'Gateway' with value 'NAT'
        var expectedTag = "cloudx";
        var subnets = await ec2Client.DescribeSubnetsAsync();

        var publicSubnet = subnets.Subnets.SingleOrDefault(s =>
            s.Tags.Exists(t => t.Key == expectedTag)
            && s.Tags.Exists(t => t.Key == "aws-cdk:subnet-type" && t.Value == "Public"));
        var privateSubnet = subnets.Subnets.SingleOrDefault(s =>
            s.Tags.Exists(t => t.Key == expectedTag)
            && s.Tags.Exists(t => t.Key == "aws-cdk:subnet-type" && t.Value == "Private"));
        // Assert
        publicSubnet.Should().NotBeNull("Public subnet was not found.");
        privateSubnet.Should().NotBeNull("Private subnet was not found.");

        var describeNatGatewaysRequest = new DescribeNatGatewaysRequest
        {
            Filter = new List<Filter>
            {
                new()
                {
                    Name = "subnet-id",
                    Values = new List<string> { publicSubnet.SubnetId }
                }
            }
        };
        var describeNatGatewaysResponse = await ec2Client.DescribeNatGatewaysAsync(describeNatGatewaysRequest);
        var describeInternetGatewaysResponse = await ec2Client.DescribeInternetGatewaysAsync();

        using (new AssertionScope())
        {
            // Validate Internet Gateway is associated with VPC
            describeInternetGatewaysResponse.InternetGateways.Should().Contain(igw => igw.Attachments.Any(att => att.VpcId == publicSubnet.VpcId),
                "Internet Gateway should be associated with VPC of existing public subnet.");
            // Validate NAT Gateway is associated with public subnet
            describeNatGatewaysResponse.NatGateways.Should().Contain(ng => ng.SubnetId == publicSubnet.SubnetId && ng.State == NatGatewayState.Available,
                "NAT Gateway should be associated with public subnet.");
        }
    }
}
