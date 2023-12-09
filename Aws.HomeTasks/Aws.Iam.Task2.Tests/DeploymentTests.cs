using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Runtime;
using Aws.Iam.Task2.Tests.Helpers;
using Aws.Iam.Task2.Tests.Models;
using FluentAssertions;
using Newtonsoft.Json;
using System.Web;

namespace Aws.Iam.Task2.Tests;

public class Tests
{
    private AmazonIdentityManagementServiceClient iamClient;

    [SetUp]
    public void Setup()
    {
        iamClient = new AmazonIdentityManagementServiceClient(CredentialsManager.GetAccessKeyId(), CredentialsManager.GetSecretAccessKey());
    }

    [Test]
    public async Task Validate_Iam_Policies()
    {
        var expectedPoliciesByName = new Dictionary<string, PolicyModel>
        {
            ["FullAccessPolicyEC2"] = new PolicyModel
            {
                Statement = new[]
                {
                    new Models.Statement
                    {
                        Action = "s3:*",
                        Effect = "Allow",
                        Resource = "*"
                    }
                }
            },
            ["FullAccessPolicyS3"] = new PolicyModel
            {
                Statement = new[]
                {
                    new Models.Statement
                    {
                        Action = "ec2:*",
                        Effect = "Allow",
                        Resource = "*"
                    }
                }
            },
            ["ReadAccessPolicyS3"] = new PolicyModel
            {
                Statement = new[]
                {
                    new Models.Statement
                    {
                        Action = new Newtonsoft.Json.Linq.JArray  { "s3:Describe*", "s3:Get*", "s3:List*" },
                        Effect = "Allow",
                        Resource = "*"
                    }
                }
            }
        };

        var listPoliciesResponse = await iamClient.ListPoliciesAsync(new ListPoliciesRequest { PolicyUsageFilter = PolicyUsageType.PermissionsPolicy });

        var returnedPoliciesByArn = new Dictionary<string, PolicyModel>();
        foreach (var policy in listPoliciesResponse.Policies)
        {
            if (expectedPoliciesByName.ContainsKey(policy.PolicyName))
            {
                returnedPoliciesByArn.Add(policy.Arn, new PolicyModel());
            }
        }

        foreach (var policy in returnedPoliciesByArn)
        {
            GetPolicyVersionResponse policyVersionResponse = await iamClient.GetPolicyVersionAsync(new GetPolicyVersionRequest
            {
                PolicyArn = policy.Key,
                VersionId = "v1"
            });
            var policyModel = JsonConvert.DeserializeObject<PolicyModel>(GetPolicyDocumentAsJson(policyVersionResponse.PolicyVersion));
            returnedPoliciesByArn[policy.Key] = policyModel;
        }

        returnedPoliciesByArn.Values.Should().BeEquivalentTo(expectedPoliciesByName.Values);
    }

    private static string GetPolicyDocumentAsJson(PolicyVersion policyVersion)
    {
        return HttpUtility.UrlDecode(policyVersion.Document);
    }
}