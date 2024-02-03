using Aws.Common.Models.API;
using Newtonsoft.Json;

namespace Aws.Common.Clients;

public class NotificationClient
{
    private readonly HttpClient httpClient;

    public NotificationClient(string hostName)
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{hostName}/api/")
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<HttpResponseMessage> CreateSubscriptionAsync(string email)
    {
        return await httpClient.PostAsync($"notification/{email}", null);
    }

    public async Task<SubscriptionModel[]> GetSubscriptionsAsync()
    {
        var response = await httpClient.GetAsync("notification");
        if (response != null && response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<SubscriptionModel[]>(responseContent);
        }
        return Array.Empty<SubscriptionModel>();
    }

    public async Task<HttpResponseMessage> DeleteSubscriptionAsync(string email)
    {
        return await httpClient.DeleteAsync($"notification/{email}");
    }
}
