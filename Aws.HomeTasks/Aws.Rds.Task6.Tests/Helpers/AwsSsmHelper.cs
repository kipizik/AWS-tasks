using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Newtonsoft.Json;

namespace Aws.Rds.Task6.Tests.Helpers;

internal static class AwsSsmHelper
{
    public static async Task<Dictionary<string, string>> GetSecretAsync(string secretName)
    {
        string secretValue = "";

        using (var client = new AmazonSecretsManagerClient())
        {
            ListSecretsResponse listSecretsResponse = await client.ListSecretsAsync(new ListSecretsRequest());
            var secretEntry = listSecretsResponse.SecretList.FirstOrDefault(entry => entry.Name.Contains(secretName));

            if (secretEntry is null)
            {
                throw new Exception($"Secrets matching string '{secretName}' was not found.");
            }

            var getSecretValueResponse = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretEntry.Name });

            if (getSecretValueResponse.SecretString != null)
            {
                secretValue = getSecretValueResponse.SecretString;
            }
            else
            {
                var decodedBinarySecret = System.Text.Encoding.UTF8.GetString(getSecretValueResponse.SecretBinary.ToArray());
                secretValue = decodedBinarySecret;
            }
        }

        var secretKeyValuePairs = JsonConvert.DeserializeObject<Dictionary<string, string>>(secretValue);

        return secretKeyValuePairs!;
    }
}
