using Amazon.EC2;
using Amazon.EC2.Model;

namespace Aws.Common.Helpers;

public static class Ec2Helper
{
    public static async Task<string> GetEc2PublicAddressAsync()
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
        var instance = describeInstancesResponse.Reservations.SelectMany(r => r.Instances).First(i => i.State.Name == InstanceStateName.Running);
        return instance.PublicIpAddress;
    }
}
