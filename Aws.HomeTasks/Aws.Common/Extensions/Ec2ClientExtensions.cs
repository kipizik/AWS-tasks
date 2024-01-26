using Amazon.EC2;
using Amazon.EC2.Model;

namespace Aws.Common.Extensions;

public static class Ec2ClientExtensions
{
    public static async Task<string> GetEc2InstanceProfileNameByTag(this AmazonEC2Client ec2Client, string tagName)
    {
        var describeInstancesResponse = await ec2Client.DescribeInstancesAsync(
            new DescribeInstancesRequest
            {
                Filters = new List<Filter>
                {
                    new()
                    {
                        Name = "tag-key",
                        Values = new List<string> { tagName }
                    }
                }
            });
        var instance = describeInstancesResponse.Reservations.SelectMany(r => r.Instances).First(i => i.State.Name == InstanceStateName.Running);
        var instanceProfileName = instance.IamInstanceProfile.Arn.Split("profile/")[1];

        return instanceProfileName;
    }
}
