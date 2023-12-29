using Aws.Common.Models;
using Newtonsoft.Json;
using System.Web;

namespace Aws.Common.Helpers;

public static class PolicyDocumentHelper
{
    public static PolicyVersionModel? GetPolicyVersionDocument(string policyVersionDocument)
    {
        var jsonString = HttpUtility.UrlDecode(policyVersionDocument);
        return JsonConvert.DeserializeObject<PolicyVersionModel>(jsonString);
    }
}
