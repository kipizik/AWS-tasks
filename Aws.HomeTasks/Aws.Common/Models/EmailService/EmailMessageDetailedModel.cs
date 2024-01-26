using Newtonsoft.Json;

namespace Aws.Common.Models.EmailService;

public class EmailMessageDetailedModel : EmailMessageModel
{
    [JsonProperty("attachments")]
    public Attachment[] Attachments { get; set; }

    [JsonProperty("body")]
    public string Body { get; set; }

    [JsonProperty("textBody")]
    public string TextBody { get; set; }

    [JsonProperty("htmlBody")]
    public string HtmlBody { get; set; }
}
