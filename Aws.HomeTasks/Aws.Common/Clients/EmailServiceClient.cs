using Aws.Common.Models.EmailService;
using Newtonsoft.Json;

namespace Aws.Common.Clients;

public class EmailServiceClient
{
    private readonly HttpClient httpClient;

    public EmailServiceClient()
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://www.1secmail.com/api/v1/")
        };
    }

    public async Task<string> GenerateRandomEmailAsync()
    {
        var responseBody = await httpClient.GetStringAsync("?action=genRandomMailbox");
        var emailAddresses = JsonConvert.DeserializeObject<string[]>(responseBody);
        if (emailAddresses!.Length == 0)
        {
            throw new Exception("Failed to generate email address.");
        }
        return emailAddresses[0];
    }

    public async Task<EmailMessageModel[]> GetMessagesAsync(string emailAddress)
    {
        var (login, domain) = GetLoginDomainFromEmailAddress(emailAddress);
        var responseBody = await httpClient.GetStringAsync($"?action=getMessages&login={login}&domain={domain}");
        var messages = JsonConvert.DeserializeObject<EmailMessageModel[]>(responseBody);

        return messages;
    }

    public async Task<EmailMessageDetailedModel> GetSingleMessageAsync(string emailAddress, int messageId)
    {
        var (login, domain) = GetLoginDomainFromEmailAddress(emailAddress);
        var responseBody = await httpClient.GetStringAsync($"?action=readMessage&login={login}&domain={domain}&id={messageId}");
        var messages = JsonConvert.DeserializeObject<EmailMessageDetailedModel>(responseBody);

        return messages;
    }

    private static (string Login, string Domain) GetLoginDomainFromEmailAddress(string emailAddress)
    {
        var emailAddressParts = emailAddress.Split('@');

        return (emailAddressParts[0], emailAddressParts[1]);
    }
}
