using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Aws.Common.Helpers;
using Aws.Common.Models;

namespace Aws.Common.Extensions;

public static class IamClientExtensions
{
    public static async Task<List<PolicyVersionModel>> GetPolicyDocumentsByIamRoleAsync(this AmazonIdentityManagementServiceClient iamClient, string roleName)
    {
        var managedPoliciesDocuments = await iamClient.GetManagedPolicyDocumentsByIamRoleAsync(roleName);
        var inlinePoliciesDocuments = await iamClient.GetInlinePolicyDocumentsByIamRoleAsync(roleName);

        return managedPoliciesDocuments.Concat(inlinePoliciesDocuments).ToList();
    }

    private static async Task<List<PolicyVersionModel>> GetManagedPolicyDocumentsByIamRoleAsync(this AmazonIdentityManagementServiceClient iamClient, string roleName)
    {
        var listRolePoliciesResponse = await iamClient.ListAttachedRolePoliciesAsync(new ListAttachedRolePoliciesRequest
        {
            RoleName = roleName,
        });
        var policyDocuments = new List<PolicyVersionModel>();
        foreach (var policy in listRolePoliciesResponse.AttachedPolicies)
        {
            var getPolicyResponse = await iamClient.GetPolicyAsync(new GetPolicyRequest { PolicyArn = policy.PolicyArn });
            var policyVersionResponse = await iamClient.GetPolicyVersionAsync(new GetPolicyVersionRequest { PolicyArn = policy.PolicyArn, VersionId = getPolicyResponse.Policy.DefaultVersionId });
            var policyVersionDocument = PolicyDocumentHelper.GetPolicyVersionDocument(policyVersionResponse.PolicyVersion.Document);
            policyDocuments.Add(policyVersionDocument!);
        }

        return policyDocuments;
    }

    private static async Task<List<PolicyVersionModel>> GetInlinePolicyDocumentsByIamRoleAsync(this AmazonIdentityManagementServiceClient iamClient, string roleName)
    {
        var listRolePoliciesRequest = new ListRolePoliciesRequest
        {
            RoleName = roleName
        };
        var rolePolicyNamesResponse = await iamClient.ListRolePoliciesAsync(listRolePoliciesRequest);
        var policyDocuments = new List<PolicyVersionModel>();

        foreach (var policyName in rolePolicyNamesResponse.PolicyNames)
        {
            var getRolePolicyRequest = new GetRolePolicyRequest
            {
                RoleName = roleName,
                PolicyName = policyName
            };

            var rolePolicyResponse = await iamClient.GetRolePolicyAsync(getRolePolicyRequest);
            var policyVersionDocument = PolicyDocumentHelper.GetPolicyVersionDocument(rolePolicyResponse.PolicyDocument);
            policyDocuments.Add(policyVersionDocument);
        }

        return policyDocuments;
    }
}
