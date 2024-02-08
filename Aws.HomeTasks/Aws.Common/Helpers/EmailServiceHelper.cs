using Amazon.SimpleNotificationService;
using Aws.Common.Clients;
using System.Web;

namespace Aws.Common.Helpers;

public static class EmailServiceHelper
{
    public static async Task ConfirmSnsSubscriptionAsync(string emailAddress, string topicArn, AmazonSimpleNotificationServiceClient snsClient)
    {
        var emailServiceClient = new EmailServiceClient();
        await WaitForMessagesAsync(emailAddress);
        var messages = await emailServiceClient.GetMessagesAsync(emailAddress);
        var confirmationMessage = await emailServiceClient.GetSingleMessageAsync(emailAddress, messages.FirstOrDefault().Id);
        var confirmationUrl = EmailParser.ExtractConfirmationURL(confirmationMessage);
        var token = HttpUtility.ParseQueryString(new Uri(confirmationUrl).Query).Get("Token");
        var response = await snsClient.ConfirmSubscriptionAsync(topicArn, token);
    }

    public static async Task WaitForMessagesAsync(string emailAddress, int messageCount = 1)
    {
        var emailServiceClient = new EmailServiceClient();
        await Waiter.WaitForAsync(
            async () => (await emailServiceClient.GetMessagesAsync(emailAddress)).Length >= messageCount,
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(15));
    }
}
