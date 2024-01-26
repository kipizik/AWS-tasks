namespace Aws.Common.Models.API;

public class SubscriptionModel
{
    public string Endpoint { get; set; }
    public string Owner { get; set; }
    public string Protocol { get; set; }
    public string SubscriptionArn { get; set; }
    public string TopicArn { get; set; }
}
