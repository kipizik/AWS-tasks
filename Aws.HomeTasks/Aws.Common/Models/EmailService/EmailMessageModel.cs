using Newtonsoft.Json;

namespace Aws.Common.Models.EmailService;

public class EmailMessageModel
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("from")]
    public string From { get; set; }

    [JsonProperty("subject")]
    public string Subject { get; set; }

    [JsonProperty("date")]
    public string Date { get; set; }
}

