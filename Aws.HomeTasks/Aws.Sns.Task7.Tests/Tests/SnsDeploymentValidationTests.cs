using Amazon.EC2;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Aws.Common.Extensions;
using Aws.Common.Models;
using FluentAssertions;
using FluentAssertions.Execution;
using NUnit.Framework;

namespace Aws.Task7.Sns.Tests.Tests;

public class SnsDeploymentValidationTests
{
    private AmazonIdentityManagementServiceClient _iamClient;
    private AmazonEC2Client _ec2Client;
    private AmazonSimpleNotificationServiceClient _snsClient;
    private AmazonSQSClient _sqsClient;

    [SetUp]
    public void Setup()
    {
        _iamClient = new AmazonIdentityManagementServiceClient();
        _ec2Client = new AmazonEC2Client();
        _snsClient = new AmazonSimpleNotificationServiceClient();
        _sqsClient = new AmazonSQSClient();
    }

    [Test]
    public async Task Application_Should_Have_Access_To_SQS_And_SNS_Via_Iam_Role()
    {
        var key = "cloudximage";
        var snsArn = await GetSNSTopicArnAsync(key);
        var sqsArn = await GetSQSQueueArnAsync(key);
        var ec2ProfileName = await _ec2Client.GetEc2InstanceProfileNameByTag("cloudx");
        var instanceProfilesResponse = await _iamClient.GetInstanceProfileAsync(
            new GetInstanceProfileRequest
            {
                InstanceProfileName = ec2ProfileName
            });

        List<PolicyVersionModel> policyDocuments = await _iamClient.GetPolicyDocumentsByIamRoleAsync(instanceProfilesResponse.InstanceProfile.Roles.Single().RoleName);
        var snsAccessPolicies = policyDocuments.Where(d => d.Statement.Any(s => s.Effect == "Allow" && snsArn.Equals(s.Resource)));
        var sqsAccessPolicies = policyDocuments.Where(d => d.Statement.Any(s => s.Effect == "Allow" && sqsArn.Equals(s.Resource)));

        using (new AssertionScope())
        {
            snsAccessPolicies.Should().Contain(p => p.Statement.Any(
                s => s.Effect == "Allow"
                && s.Action.ToString()!.Contains("sns:Publish")
                ));
            snsAccessPolicies.Should().Contain(p => p.Statement.Any(
                s => s.Effect == "Allow"
                && s.Action.ToString()!.Contains("sns:ListSubscriptions")
                && s.Action.ToString()!.Contains("sns:Subscribe")
                && s.Action.ToString()!.Contains("sns:Unsubscribe")
                ));

            sqsAccessPolicies.Should().Contain(p => p.Statement.Any(
                s => s.Effect == "Allow"
                && s.Action.ToString()!.Contains("sqs:ChangeMessageVisibility")
                && s.Action.ToString()!.Contains("sqs:DeleteMessage")
                && s.Action.ToString()!.Contains("sqs:GetQueueAttributes")
                && s.Action.ToString()!.Contains("sqs:GetQueueUrl")
                && s.Action.ToString()!.Contains("sqs:ReceiveMessage")
                && s.Action.ToString()!.Contains("sqs:SendMessage")
                ));
        }
    }

    [Test]
    public async Task Validate_SNS_Topic_Requirements()
    {
        var topicNamePrefix = "cloudximage-TopicSNSTopic";
        var listTopicResponse = await _snsClient.ListTopicsAsync();
        var topicArn = listTopicResponse.Topics.SingleOrDefault(x => x.TopicArn.Contains(topicNamePrefix))!.TopicArn;

        // Assert topic existence
        topicArn.Should().NotBeNull($"SNS Topic with name ending with '{topicNamePrefix}' should exist.");

        // Assert encryption (By default, SNS encryption is disabled)
        var topicAttributes = await _snsClient.GetTopicAttributesAsync(topicArn);
        topicAttributes.Attributes.Keys.Should().NotContain(QueueAttributeName.KmsMasterKeyId, "SNS Topic encryption should be disabled.");

        // Assert tags
        var topicTagsResponse = await _snsClient.ListTagsForResourceAsync(new ListTagsForResourceRequest { ResourceArn = topicArn });
        topicTagsResponse.Tags.Any(t => t.Key == "cloudx").Should().BeTrue("SNS Topic should have tag 'cloudx'.");
    }

    [Test]
    public async Task Validate_SQS_Queue_Requirements()
    {
        var queueNamePrefix = "cloudximage-QueueSQSQueue";
        var listQueuesResponse = await _sqsClient.ListQueuesAsync(queueNamePrefix);
        listQueuesResponse.QueueUrls.Should().HaveCount(1, $"SQS queue with name '{queueNamePrefix}' should exist");
        var queueUrl = listQueuesResponse.QueueUrls.Single();

        // Assert queue existence
        queueUrl.Should().NotBeNullOrEmpty($"SQS queue containing '{queueNamePrefix}' should exist");

        // Assert encryption
        var attributesResponse = await _sqsClient.GetQueueAttributesAsync(
            new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = new List<string> { "All" } });
        attributesResponse.Attributes.Keys.Should()
            .Contain(key => key == QueueAttributeName.KmsMasterKeyId || key == QueueAttributeName.SqsManagedSseEnabled, "Encryption should be enabled.");
        attributesResponse.Attributes.Should().NotContainKey(QueueAttributeName.FifoQueue);

        // Assert tags
        var listTagsRequest = new ListQueueTagsRequest { QueueUrl = queueUrl };
        var tagsResponse = await _sqsClient.ListQueueTagsAsync(listTagsRequest);
        tagsResponse.Tags.Keys.Should().Contain("cloudx", "SQS queue should have tag 'cloudx'");

        // Assert dead-letter queue absence
        attributesResponse.Attributes.Should().NotContainKey(QueueAttributeName.RedrivePolicy, "SQS should not have dead-letter queue.");
    }

    private static async Task<string> GetSQSQueueArnAsync(string queueNamePrefix)
    {
        var sqsClient = new AmazonSQSClient();
        var response = await sqsClient.ListQueuesAsync(queueNamePrefix);
        var attributesResponse = await sqsClient.GetQueueAttributesAsync(response.QueueUrls.Single(), new List<string> { "QueueArn" });

        return attributesResponse.QueueARN;
    }

    private async Task<string> GetSNSTopicArnAsync(string topicNamePart)
    {
        var snsClient = new AmazonSimpleNotificationServiceClient();

        var listTopicsResponse = await snsClient.ListTopicsAsync();
        var topicArn = listTopicsResponse.Topics.Single(t => t.TopicArn.Contains(topicNamePart)).TopicArn;

        return topicArn;
    }
}