using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Aws.Common.Clients;
using Aws.Common.Helpers;
using Aws.Common.Models;
using Aws.Common.Models.API;
using Aws.Common.Models.EmailService;
using FluentAssertions;
using FluentAssertions.Execution;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Web;

namespace Aws.Sns.Task7.Tests.Tests;

public class SnsFunctionalTests
{
    private NotificationClient _notificationApiClient;
    private AmazonSimpleNotificationServiceClient _snsClient;
    private EmailServiceClient _emailServiceClient;
    private ImageClient _imageClient;
    private string _userEmail;
    private Topic? _snsTopic;

    [SetUp]
    public async Task BeforeTest()
    {
        string address = await GetEc2PublicAddress();
        _notificationApiClient = new NotificationClient(address);
        _emailServiceClient = new EmailServiceClient();
        _imageClient = new ImageClient(address);

        _snsClient = new AmazonSimpleNotificationServiceClient();
        _userEmail = await _emailServiceClient.GenerateRandomEmailAsync();

        var topicNamePrefix = "cloudximage";
        var listTopicsResponse = await _snsClient.ListTopicsAsync();
        _snsTopic = listTopicsResponse.Topics.SingleOrDefault(t => t.TopicArn.Split(':').Last().StartsWith(topicNamePrefix));

        _snsTopic.Should().NotBeNull($"Topic starting with {topicNamePrefix} not found.");
    }

    [TearDown]
    public async Task AfterTest()
    {
        var listSubscriptionsResponse = await _snsClient.ListSubscriptionsAsync();
        foreach (var subscription in listSubscriptionsResponse.Subscriptions)
        {
            if (subscription.SubscriptionArn != "Deleted" && subscription.SubscriptionArn != "PendingConfirmation")
            {
                await DeleteSubscriptionAsync(subscription.SubscriptionArn);
            }
        }
    }

    [Test]
    public async Task User_Can_Subscribe_To_Notifications_About_Application_Events()
    {
        // create a subscription through exposed application API
        var createResponse = await _notificationApiClient.CreateSubscriptionAsync(_userEmail);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // verify that subscription exists through SDK
        var listSubscriptionsResponse = await _snsClient.ListSubscriptionsByTopicAsync(_snsTopic.TopicArn);
        var subscription = listSubscriptionsResponse.Subscriptions.SingleOrDefault(s => s.Endpoint == _userEmail);

        subscription.Should().NotBeNull($"No subscription found for email {_userEmail} in topic {_snsTopic.TopicArn}");
    }

    [Test]
    public async Task User_Has_To_Confirm_The_Subscription_After_Receiving_The_Confirmation_Email()
    {
        // create a subscription through exposed application API
        var createResponse = await _notificationApiClient.CreateSubscriptionAsync(_userEmail);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // verify that email is received
        await Waiter.WaitForAsync(
            async () => (await _emailServiceClient.GetMessagesAsync(_userEmail)).Any(),
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(15));
        var messages = await _emailServiceClient.GetMessagesAsync(_userEmail);
        var confirmationMessage = messages.FirstOrDefault(m => m.Subject == "AWS Notification - Subscription Confirmation");

        confirmationMessage.Should().NotBeNull($"Confirmation email was not received for email address {_userEmail}.");
    }

    [Test]
    public async Task User_Receives_Notifications_About_Images_Events()
    {
        // Arrange
        // create a subscription through exposed application API
        var createResponse = await _notificationApiClient.CreateSubscriptionAsync(_userEmail);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await ConfirmSubscriptionAsync(_userEmail, _snsTopic.TopicArn);

        // Act
        // upload an image
        var expectedImages = Directory.EnumerateFiles(_imageClient.ImageDirectory);
        var fileName = expectedImages.Select(image => Path.GetFileName(image)).FirstOrDefault();
        var imageId = await _imageClient.UploadImageAsync(fileName);
        // delete an image
        var deleteImageMessage = await _imageClient.DeleteImageAsync(imageId);

        await WaitForMessagesAsync(_userEmail, 2);
        var receivedEmailMessages = await _emailServiceClient.GetMessagesAsync(_userEmail);
        var notificationMessageIds = receivedEmailMessages
            .Where(message => message.Subject == "AWS Notification Message")
            .Select(m => m.Id);
        var notificationMessages = await Task.WhenAll(notificationMessageIds.Select(async id => await _emailServiceClient.GetSingleMessageAsync(_userEmail, id)));
        var notificationEventDetails = notificationMessages.Select(m => EmailParser.GetEventDetails(m.Body));

        // Assert
        using (new AssertionScope())
        {
            notificationEventDetails.Should().ContainSingle(e =>
                e.EventType == "upload"
                && e.ObjectKey.Contains(fileName)
                && e.DownloadLink.Contains($"image/file/{imageId}"));
            notificationEventDetails.Should().ContainSingle(e =>
                e.EventType == "delete"
                && e.ObjectKey.Contains(fileName));
        }
    }

    [Test]
    public async Task User_Can_Download_The_Image_Using_The_Download_Link_From_The_Notification()
    {
        // Arrange
        // create a subscription through exposed application API
        var createResponse = await _notificationApiClient.CreateSubscriptionAsync(_userEmail);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await ConfirmSubscriptionAsync(_userEmail, _snsTopic.TopicArn);

        // Act
        // upload an image
        var expectedImages = Directory.EnumerateFiles(_imageClient.ImageDirectory);
        var fileName = expectedImages.Select(image => Path.GetFileName(image)).FirstOrDefault();
        var imageId = await _imageClient.UploadImageAsync(fileName);

        await WaitForMessagesAsync(_userEmail);
        var receivedEmailMessages = await _emailServiceClient.GetMessagesAsync(_userEmail);
        var notificationMessageIds = receivedEmailMessages
            .Where(message => message.Subject == "AWS Notification Message")
            .Select(m => m.Id);
        var notificationMessages = await Task.WhenAll(notificationMessageIds.Select(async id => await _emailServiceClient.GetSingleMessageAsync(_userEmail, id)));
        var notificationEventDetails = notificationMessages.Select(m => EmailParser.GetEventDetails(m.Body));

        notificationEventDetails.Should().ContainSingle(e =>
                e.EventType == "upload"
                && e.ObjectKey.Contains(fileName)
                && e.DownloadLink.Contains($"image/file/{imageId}"));

        var downloadLink = notificationEventDetails.Single(e => e.EventType == "upload").DownloadLink;
        var downloadedImage = await new HttpClient().GetByteArrayAsync(downloadLink);
        var expectedImage = File.ReadAllBytes(expectedImages.First());

        downloadedImage.Should().BeEquivalentTo(expectedImage);
    }

    [Test]
    public async Task User_Can_Unsubscribe_From_The_Notifications()
    {
        // Arrange
        // create a subscription through SDK
        var subscribeRequest = new SubscribeRequest
        {
            TopicArn = _snsTopic.TopicArn,
            Protocol = "email",
            Endpoint = _userEmail
        };
        var subscribeResponse = await _snsClient.SubscribeAsync(subscribeRequest);
        subscribeResponse.HttpStatusCode.Should().Be(HttpStatusCode.OK, $"Failed to subscribe {_userEmail} to topic {_snsTopic.TopicArn}");

        await ConfirmSubscriptionAsync(_userEmail, _snsTopic.TopicArn);

        // Act
        var deleteSubscriptionResponse = await _notificationApiClient.DeleteSubscriptionAsync(_userEmail);
        deleteSubscriptionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert
        // verify that subscription doesn't exist through SDK
        var listSubscriptionsResponse = await _snsClient.ListSubscriptionsByTopicAsync(_snsTopic.TopicArn);
        var subscription = listSubscriptionsResponse.Subscriptions.SingleOrDefault(s => s.Endpoint == _userEmail);

        subscription.Should().BeNull($"Subscription shouldn't exist for email {_userEmail} in topic {_snsTopic.TopicArn}");
    }

    [Test]
    public async Task Unsubscribed_User_Does_Not_Receive_Further_Notifications()
    {
        // Arrange
        // create a subscription through SDK
        var subscribeRequest = new SubscribeRequest
        {
            TopicArn = _snsTopic.TopicArn,
            Protocol = "email",
            Endpoint = _userEmail
        };
        var subscribeResponse = await _snsClient.SubscribeAsync(subscribeRequest);
        subscribeResponse.HttpStatusCode.Should().Be(HttpStatusCode.OK, $"Failed to subscribe {_userEmail} to topic {_snsTopic.TopicArn}");

        await ConfirmSubscriptionAsync(_userEmail, _snsTopic.TopicArn);

        // Act
        // Unsubscribe
        var deleteSubscriptionResponse = await _notificationApiClient.DeleteSubscriptionAsync(_userEmail);
        deleteSubscriptionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // upload an image
        var expectedImages = Directory.EnumerateFiles(_imageClient.ImageDirectory);
        var fileName = expectedImages.Select(image => Path.GetFileName(image)).FirstOrDefault();
        var imageId = await _imageClient.UploadImageAsync(fileName);

        // Assert
        await WaitForMessagesAsync(_userEmail);
        var receivedEmailMessages = await _emailServiceClient.GetMessagesAsync(_userEmail);
        var notificationMessages = receivedEmailMessages
            .Where(message => message.Subject == "AWS Notification Message");

        notificationMessages.Should().BeEmpty();
    }

    [Test]
    public async Task View_Existing_Subscriptions()
    {
        // Arrange
        // create a subscription through SDK
        var subscribeRequest = new SubscribeRequest
        {
            TopicArn = _snsTopic.TopicArn,
            Protocol = "email",
            Endpoint = _userEmail
        };
        var response = await _snsClient.SubscribeAsync(subscribeRequest);
        response.HttpStatusCode.Should().Be(HttpStatusCode.OK, $"Failed to subscribe {_userEmail} to topic {_snsTopic.TopicArn}");

        // Act
        var apiSubscriptions = await _notificationApiClient.GetSubscriptionsAsync();

        // Assert
        // verify that subscriptions returned through API match the ones returned through SDK
        var listSubscriptionsResponse = await _snsClient.ListSubscriptionsByTopicAsync(_snsTopic.TopicArn);
        var snsSubscriptions = listSubscriptionsResponse.Subscriptions.Select(s =>
            new SubscriptionModel
            {
                Endpoint = s.Endpoint,
                Owner = s.Owner,
                Protocol = s.Protocol,
                SubscriptionArn = s.SubscriptionArn,
                TopicArn = s.TopicArn
            });

        apiSubscriptions.Should().BeEquivalentTo(snsSubscriptions);
    }

    private async Task ConfirmSubscriptionAsync(string emailAddress, string topicArn)
    {
        await WaitForMessagesAsync(emailAddress);
        var messages = await _emailServiceClient.GetMessagesAsync(emailAddress);
        var confirmationMessage = await _emailServiceClient.GetSingleMessageAsync(emailAddress, messages.FirstOrDefault().Id);
        var confirmationUrl = EmailParser.ExtractConfirmationURL(confirmationMessage);
        var token = HttpUtility.ParseQueryString(new Uri(confirmationUrl).Query).Get("Token");
        var response = await _snsClient.ConfirmSubscriptionAsync(topicArn, token);
    }

    private static async Task<string> GetEc2PublicAddress()
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

    private async Task DeleteSubscriptionAsync(string subscriptionArn)
    {
        using var client = new AmazonSimpleNotificationServiceClient();
        var unsubscribeRequest = new UnsubscribeRequest
        {
            SubscriptionArn = subscriptionArn
        };

        var response = await client.UnsubscribeAsync(unsubscribeRequest);

        if (response.HttpStatusCode == HttpStatusCode.OK)
        {
            Console.WriteLine($"Successfully deleted subscription {subscriptionArn}");
        }
    }

    private async Task WaitForMessagesAsync(string emailAddress, int messageCount = 1)
    {
        await Waiter.WaitForAsync(
            async () => (await _emailServiceClient.GetMessagesAsync(emailAddress)).Length >= messageCount,
            retryDelay: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(15));
    }
}