using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using FluentAssertions;
using NUnit.Framework;

namespace Aws.Task9.CloudWatch.Tests;

public class FunctionalTests
{
    private AmazonCloudWatchLogsClient _cloudWatchLogsClient;

    [OneTimeSetUp]
    public void Setup()
    {
        _cloudWatchLogsClient = new AmazonCloudWatchLogsClient();
    }

    [Test]
    public async Task Notification_Events_Are_Logged_In_CloudWatch()
    {
        var serverlessLogGroupsRequest = new DescribeLogGroupsRequest
        {
            LogGroupNamePattern = "cloudxserverless-EventHandlerLambda",
        };
        // get log group names
        var describeLogGroupsResponse = await _cloudWatchLogsClient.DescribeLogGroupsAsync(serverlessLogGroupsRequest);
        var logGroupNames = describeLogGroupsResponse.LogGroups.Select(lg => lg.LogGroupName);
        var logEvents = new List<FilteredLogEvent>();
        foreach (var groupName in logGroupNames)
        {
            var request = new FilterLogEventsRequest
            {
                LogGroupName = groupName
            };
            var filterLogEventsResponse = await _cloudWatchLogsClient.FilterLogEventsAsync(request);
            logEvents.AddRange(filterLogEventsResponse.Events);
        }
        string[] filterStrings = new[] { "HANDLER: event=", "HANDLER: records=" };
        var filteredLogEvents = logEvents.Where(e => filterStrings.Any(fs => e.Message.Contains(fs)));

        filteredLogEvents.Should().NotBeEmpty("Each notification event processed by Event Handler Lambda should be logged in the CloudWatch logs");

        // image information is logged
        var expectedImageDataKeys = new[] { "object_key", "object_size", "object_type", "last_modified" };
        var handlerEventMessages = logEvents
            .Where(e => e.Message.Contains("HANDLER: event="))
            .Select(e => e.Message);

        // Assert
        handlerEventMessages
            .Any(m => expectedImageDataKeys.All(key => m.Contains(key)))
            .Should()
            .BeTrue("Image information (object key, object type, object size, modification date, download link) should be logged in the Event Handler Lambda logs in CloudWatch");
    }

    [Test]
    public async Task Api_Requests_Are_Logged_In_CloudWatch()
    {
        var serverlessLogGroupsRequest = new DescribeLogGroupsRequest
        {
            LogGroupNamePattern = "cloudxserverless-app",
        };
        var describeLogGroupsResponse = await _cloudWatchLogsClient.DescribeLogGroupsAsync(serverlessLogGroupsRequest);
        var logGroupNames = describeLogGroupsResponse.LogGroups.Select(lg => lg.LogGroupName);
        var logEvents = new List<FilteredLogEvent>();
        // get log streams
        var apiRequestsPatterns = new[]
        {
            "POST /api/image",
            "GET /api/image",
            "GET /api/images",
            "DELETE /api/image",
            "POST /api/notification",
            "GET /api/notification",
            "DELETE /api/notification",
        };
        foreach (var groupName in logGroupNames)
        {
            var request = new FilterLogEventsRequest
            {
                LogGroupName = groupName
            };
            var filterLogEventsResponse = await _cloudWatchLogsClient.FilterLogEventsAsync(request);
            logEvents.AddRange(filterLogEventsResponse.Events);
        }
        var filteredLogEvents = logEvents.Where(e => apiRequestsPatterns.Any(p => e.Message.Contains(p)));

        filteredLogEvents.Should().NotBeEmpty("All HTTP API requests processed by the application should be logged in the CloudWatch logs.");
    }
}