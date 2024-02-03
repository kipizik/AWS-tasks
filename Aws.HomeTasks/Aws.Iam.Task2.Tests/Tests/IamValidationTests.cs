using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Aws.Common.Helpers;
using Aws.Common.Models;
using FluentAssertions;
using FluentAssertions.Execution;
using NUnit.Framework;

namespace Aws.Task2.Iam.Tests.Tests;

public class IamValidationTests
{
    private const string FullAccessPolicyEC2 = "FullAccessPolicyEC2";
    private const string FullAccessPolicyS3 = "FullAccessPolicyS3";
    private const string ReadAccessPolicyS3 = "ReadAccessPolicyS3";

    private AmazonIdentityManagementServiceClient iamClient;

    [SetUp]
    public void Setup()
    {
        iamClient = new AmazonIdentityManagementServiceClient(CredentialsManager.GetAccessKeyId(), CredentialsManager.GetSecretAccessKey());
    }

    [Test]
    public async Task Validate_Iam_Policies()
    {
        var expectedPoliciesByName = new Dictionary<string, PolicyVersionModel>
        {
            [FullAccessPolicyEC2] = new PolicyVersionModel
            {
                Statement = new[]
                {
                    new Common.Models.Statement
                    {
                        Action = "ec2:*",
                        Effect = "Allow",
                        Resource = "*"
                    }
                }
            },
            [FullAccessPolicyS3] = new PolicyVersionModel
            {
                Statement = new[]
                {
                    new Common.Models.Statement
                    {
                        Action = "s3:*",
                        Effect = "Allow",
                        Resource = "*"
                    }
                }
            },
            [ReadAccessPolicyS3] = new PolicyVersionModel
            {
                Statement = new[]
                {
                    new Common.Models.Statement
                    {
                        Action = new Newtonsoft.Json.Linq.JArray  { "s3:Describe*", "s3:Get*", "s3:List*" },
                        Effect = "Allow",
                        Resource = "*"
                    }
                }
            }
        };

        var listPoliciesResponse = await iamClient.ListPoliciesAsync(new ListPoliciesRequest { PolicyUsageFilter = PolicyUsageType.PermissionsPolicy });

        var returnedPoliciesByArn = new Dictionary<string, PolicyVersionModel>();
        foreach (var policy in listPoliciesResponse.Policies)
        {
            if (expectedPoliciesByName.ContainsKey(policy.PolicyName))
            {
                returnedPoliciesByArn.Add(policy.Arn, new PolicyVersionModel());
            }
        }

        foreach (var policy in returnedPoliciesByArn)
        {
            GetPolicyVersionResponse policyVersionResponse = await iamClient.GetPolicyVersionAsync(new GetPolicyVersionRequest
            {
                PolicyArn = policy.Key,
                VersionId = "v1"
            });
            var policyModel = PolicyDocumentHelper.GetPolicyVersionDocument(policyVersionResponse.PolicyVersion.Document);
            returnedPoliciesByArn[policy.Key] = policyModel!;
        }

        returnedPoliciesByArn.Values.Should().BeEquivalentTo(expectedPoliciesByName.Values);
    }

    [Test]
    public async Task Validate_Attached_Policy_Roles()
    {
        var expectedRolesByPolicyName = new Dictionary<string, string>
        {
            [FullAccessPolicyEC2] = "FullAccessRoleEC2",
            [FullAccessPolicyS3] = "FullAccessRoleS3",
            [ReadAccessPolicyS3] = "ReadAccessRoleS3"
        };

        using (new AssertionScope())
        {
            foreach (var pair in expectedRolesByPolicyName)
            {
                var attachedRolePoliciesResponse = await iamClient.ListAttachedRolePoliciesAsync(new ListAttachedRolePoliciesRequest { RoleName = pair.Value });

                attachedRolePoliciesResponse.AttachedPolicies.Should().HaveCount(1);
                attachedRolePoliciesResponse.AttachedPolicies.Single().PolicyName.Should().Be(pair.Key);
            }
        }
    }

    [Test]
    public async Task Validate_Attached_Policy_UserGroups()
    {
        var expectedGroupsByPolicyName = new Dictionary<string, string>
        {
            [FullAccessPolicyEC2] = "FullAccessGroupEC2",
            [FullAccessPolicyS3] = "FullAccessGroupS3",
            [ReadAccessPolicyS3] = "ReadAccessGroupS3"
        };

        using (new AssertionScope())
        {
            foreach (var pair in expectedGroupsByPolicyName)
            {
                var attachedRolePoliciesResponse = await iamClient.ListAttachedGroupPoliciesAsync(new ListAttachedGroupPoliciesRequest { GroupName = pair.Value });

                attachedRolePoliciesResponse.AttachedPolicies.Should().HaveCount(1);
                attachedRolePoliciesResponse.AttachedPolicies.Single().PolicyName.Should().Be(pair.Key);
            }
        }
    }

    [Test]
    public async Task Validate_Created_Users()
    {
        var expectedUsersByGroupName = new Dictionary<string, string>
        {
            ["FullAccessUserEC2"] = "FullAccessGroupEC2",
            ["FullAccessUserS3"] = "FullAccessGroupS3",
            ["ReadAccessUserS3"] = "ReadAccessGroupS3"
        };

        using (new AssertionScope())
        {
            foreach (var pair in expectedUsersByGroupName)
            {
                var getGroupResponse = await iamClient.GetGroupAsync(new GetGroupRequest { GroupName = pair.Value });

                getGroupResponse.Users.Should().HaveCount(1);
                getGroupResponse.Users.Single().UserName.Should().Be(pair.Key);
            }
        }
    }
}