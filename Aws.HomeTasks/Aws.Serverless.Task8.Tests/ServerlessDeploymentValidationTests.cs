using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.IdentityManagement;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Aws.Common.Clients;
using Aws.Common.Extensions;
using Aws.Common.Helpers;
using Aws.Common.Models;
using Aws.Common.Models.API;
using FluentAssertions;
using FluentAssertions.Execution;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Reflection;

namespace Aws.Task8.Serverless.Tests;

public class ServerlessDeploymentValidationTests
{
    private const string _expectedTag = "cloudx";
    private AmazonDynamoDBClient _dynamoDbClient;
    private AmazonSimpleNotificationServiceClient _snsClient;
    private AmazonSQSClient _sqsClient;
    private AmazonLambdaClient _lambdaClient;
    private AmazonIdentityManagementServiceClient _iamClient;
    private ImageClient _imageApiClient;
    private Topic? _snsTopic;
    private string? _sqsQueueUrl;

    [OneTimeSetUp]
    public async Task Setup()
    {
        _dynamoDbClient = new AmazonDynamoDBClient();
        _snsClient = new AmazonSimpleNotificationServiceClient();
        _sqsClient = new AmazonSQSClient();
        _lambdaClient = new AmazonLambdaClient();
        _iamClient = new AmazonIdentityManagementServiceClient();

        var hostName = await Ec2Helper.GetEc2PublicAddressAsync();
        _imageApiClient = new ImageClient(hostName);

        var listTopicsResponse = await _snsClient.ListTopicsAsync();
        var topicNamePrefixes = new[] { "cloudximage", "cloudxserverless" };
        _snsTopic = listTopicsResponse.Topics
            .SingleOrDefault(t => topicNamePrefixes.Any(prefix => t.TopicArn.Split(':').Last().StartsWith(prefix)));

        var queueNamePrefix = _expectedTag;
        var listQueuesResponse = await _sqsClient.ListQueuesAsync(queueNamePrefix);
        _sqsQueueUrl = listQueuesResponse.QueueUrls.SingleOrDefault();
    }

    [Test]
    public async Task DynamoDB_Table_Should_Store_Correct_Image_Metadata()
    {
        // upload an image so a new item is added to DynamoDB
        var expectedImages = Directory.EnumerateFiles(_imageApiClient.ImageDirectory);
        var fileName = expectedImages.Select(image => Path.GetFileName(image)).FirstOrDefault();
        var uploadedImageId = await _imageApiClient.UploadImageAsync(fileName);

        var expectedItemAttributes = GetItemAttributes();

        // Get the items and validate the inserted data
        var dbData = await DbHelper.GetDataFromNoSqlDbAsync();
        dbData.Should().NotBeEmpty();
        var dbItemAttributes = dbData.First().Keys.Select(k => k);

        // Check each of the object details
        dbItemAttributes.Should().BeEquivalentTo(expectedItemAttributes);
    }

    [Test]
    public async Task Lambda_Function_Is_Subscribed_To_SQS()
    {
        var getQueueAttributesResponse = await _sqsClient.GetQueueAttributesAsync(_sqsQueueUrl, new List<string> { "QueueArn" });
        var sqsArn = getQueueAttributesResponse.QueueARN;
        var getFunctionConfigurationResponse = await GetFunctionConfigurationAsync("EventHandlerLambda");

        // Act
        string[] splitRoleArn = getFunctionConfigurationResponse.Role.Split('/');
        var roleName = splitRoleArn[splitRoleArn.Length - 1];
        List<PolicyVersionModel> policyDocuments = await _iamClient.GetPolicyDocumentsByIamRoleAsync(roleName);
        var sqsAccessPolicies = policyDocuments.Where(d => d.Statement.Any(s => s.Effect == "Allow" && sqsArn.Equals(s.Resource)));

        // Assert
        sqsAccessPolicies.Should().Contain(p => p.Statement.Any(
            s => s.Effect == "Allow"
            && s.Action.ToString()!.Contains("sqs:ChangeMessageVisibility")
            && s.Action.ToString()!.Contains("sqs:ReceiveMessage")
            ));
    }

    [Test]
    public async Task Lambda_Function_Put_Event_Messages_To_SNS()
    {
        // Act
        var getFunctionConfigurationResponse = await GetFunctionConfigurationAsync("EventHandlerLambda");
        string[] splitRoleArn = getFunctionConfigurationResponse.Role.Split('/');
        var roleName = splitRoleArn[splitRoleArn.Length - 1];
        List<PolicyVersionModel> policyDocuments = await _iamClient.GetPolicyDocumentsByIamRoleAsync(roleName);
        var snsAccessPolicies = policyDocuments.Where(d => d.Statement.Any(s => s.Effect == "Allow" && _snsTopic.TopicArn.Equals(s.Resource)));

        // Assert
        snsAccessPolicies.Should().Contain(p => p.Statement.Any(
            s => s.Effect == "Allow"
            && s.Action.ToString()!.Contains("sns:Publish")
            ));
    }

    [Test]
    public async Task Verify_Lambda_Function_Requirements()
    {
        // Fetch the configuration of the specific lambda function
        var getFunctionConfigurationResponse = await GetFunctionConfigurationAsync("EventHandlerLambda");
        var listTagsResponse = await _lambdaClient.ListTagsAsync(new ListTagsRequest { Resource = getFunctionConfigurationResponse.FunctionArn });


        using (new AssertionScope())
        {
            // Assert memory configuration
            getFunctionConfigurationResponse.MemorySize.Should().Be(128, "Memory size should be 128 MB");
            // Assert timeout configuration
            getFunctionConfigurationResponse.Timeout.Should().Be(3, "Timeout should be 3 sec");
            // Assert log group configuration
            getFunctionConfigurationResponse.LoggingConfig.LogGroup.Should().Contain("aws/lambda/cloudxserverless-EventHandlerLambda");
            // Assert ephemeral storage 
            getFunctionConfigurationResponse.EphemeralStorage.Size.Should().Be(512, "Ephemeral storage size should be 512 MB");
            // Assert Tags
            listTagsResponse.Tags.Should().ContainKey(_expectedTag);
        }
    }

    [Test]
    public async Task Verify_DynamoDb_Table_Requirements()
    {
        var listTablesResponse = await _dynamoDbClient.ListTablesAsync();
        var tableName = listTablesResponse.TableNames.FirstOrDefault();
        if (string.IsNullOrEmpty(tableName))
        {
            throw new Exception("No DB tables were found.");
        }
        var describeTableResponse = await _dynamoDbClient.DescribeTableAsync(
            new DescribeTableRequest
            {
                TableName = tableName
            });
        var describeTimeToLiveResponse = await _dynamoDbClient.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = tableName });
        var tableTagsResponse = await _dynamoDbClient.ListTagsOfResourceAsync(new ListTagsOfResourceRequest { ResourceArn = describeTableResponse.Table.TableArn });

        // Assert
        using (new AssertionScope())
        {
            // Verify Required Table Properties
            var table = describeTableResponse.Table;
            table.TableName.Should().Be(tableName);
            table.ProvisionedThroughput.ReadCapacityUnits.Should().Be(5);
            table.ProvisionedThroughput.WriteCapacityUnits.Should().Be(1);
            table.GlobalSecondaryIndexes.Should().BeNullOrEmpty();

            // Verify TTL
            describeTimeToLiveResponse.TimeToLiveDescription.TimeToLiveStatus.Should().Be(TimeToLiveStatus.DISABLED);

            // Verify Tags
            tableTagsResponse.Tags.Should().Contain(t => t.Key == _expectedTag);
        }
    }

    private async Task<GetFunctionConfigurationResponse> GetFunctionConfigurationAsync(string functionNamePrefix)
    {
        var listFunctionsResponse = await _lambdaClient.ListFunctionsAsync();
        var functionName = listFunctionsResponse.Functions.SingleOrDefault(f => f.FunctionName.Contains(functionNamePrefix)).FunctionName;
        var getFunctionConfigurationResponse = await _lambdaClient.GetFunctionConfigurationAsync(functionName);

        return getFunctionConfigurationResponse;
    }

    private static IEnumerable<string> GetItemAttributes()
    {
        // Get properties using reflection
        PropertyInfo[] properties = typeof(ImageModel).GetProperties();

        // Store JsonProperty values
        List<string> jsonPropertyValues = new();

        foreach (PropertyInfo property in properties)
        {
            // Get JsonProperty attribute from property
            JsonPropertyAttribute attr = property.GetCustomAttribute<JsonPropertyAttribute>();

            if (attr != null)
            {
                // Add JsonProperty value to list
                jsonPropertyValues.Add(attr.PropertyName);
            }
        }

        return jsonPropertyValues;
    }
}