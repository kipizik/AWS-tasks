using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.RDS;
using Amazon.RDS.Model;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Aws.Rds.Task6.Tests;

[TestFixture]
internal class RdsDeploymentValidationTests
{
    private AmazonRDSClient rdsClient;
    private AmazonEC2Client ec2Client;

    [SetUp]
    public void Init()
    {
        rdsClient = new AmazonRDSClient();
        ec2Client = new AmazonEC2Client();
    }

    [Test]
    public async Task RDSInstance_Should_Be_In_Private_Subnet()
    {
        var dbInstanceResponse = await rdsClient.DescribeDBInstancesAsync();
        var dbInstance = dbInstanceResponse.DBInstances.FirstOrDefault();

        dbInstance.PubliclyAccessible.Should().BeFalse();

        var describeDbSubnetGroupRequest = new DescribeDBSubnetGroupsRequest
        {
            DBSubnetGroupName = dbInstance.DBSubnetGroup.DBSubnetGroupName
        };
        var dbSubnetGroupResponse = await rdsClient.DescribeDBSubnetGroupsAsync(describeDbSubnetGroupRequest);
        var subnetIds = dbSubnetGroupResponse.DBSubnetGroups.SelectMany(sg => sg.Subnets.Select(s => s.SubnetIdentifier)).ToList();

        var routeTableResponse = await ec2Client.DescribeRouteTablesAsync(new DescribeRouteTablesRequest());
        var routeTables = routeTableResponse.RouteTables.Where(rt => rt.Associations.Any(a => subnetIds.Contains(a.SubnetId)));
        routeTables.Should().AllSatisfy(routeTable =>
        {
            routeTable.Should().NotBeNull();
            routeTable.Routes.Should().Match(routes => routes.Any(route => route.GatewayId == "local" && !route.GatewayId!.StartsWith("igw-")));
        });
    }

    [Test]
    public async Task RDSInstance_Should_Be_Accessible_By_App_Instance()
    {
        var dbInstanceResponse = await rdsClient.DescribeDBInstancesAsync();
        var dbInstance = dbInstanceResponse.DBInstances.FirstOrDefault();
        var dbSecurityGroupId = dbInstance.VpcSecurityGroups.Single().VpcSecurityGroupId;

        var ec2InstanceResponse = await ec2Client.DescribeInstancesAsync();
        var ec2SecurityGroupIds = ec2InstanceResponse.Reservations.Single().Instances.Single().SecurityGroups.Select(sg => sg.GroupId);

        var securityGroupsResponse = await ec2Client.DescribeSecurityGroupsAsync(
            new DescribeSecurityGroupsRequest { GroupIds = new List<string> { dbSecurityGroupId } });
        var dbSecurityGroup = securityGroupsResponse.SecurityGroups.FirstOrDefault(sg => sg.GroupId == dbSecurityGroupId);

        dbSecurityGroup.IpPermissions.Should().Contain(permission => permission.UserIdGroupPairs.Any(gp => ec2SecurityGroupIds.Contains(gp.GroupId)));
    }

    [Test]
    public async Task RDSInstance_Should_Match_Required_Configuration()
    {
        var response = await rdsClient.DescribeDBInstancesAsync();
        var instance = response.DBInstances.FirstOrDefault();

        instance.Should().NotBeNull("One DB instance should exist.");

        using (new AssertionScope())
        {
            instance!.DBInstanceClass.Should().Be("db.t3.micro");
            instance.MultiAZ.Should().BeFalse();
            instance.AllocatedStorage.Should().Be(100);
            instance.StorageEncrypted.Should().BeFalse("DB storage should not be encrypted");
            instance.StorageType.Should().Be("gp2", "DB storage type should be gp2");
            instance.TagList.Should().Contain(tag => tag.Key == "cloudx");
            instance.Engine.Should().Be("mysql");
            instance.EngineVersion.Should().Be("8.0.28");
        }
    }
}
