using Newtonsoft.Json;

namespace Aws.Common.Models.EmailService;

public class Attachment
{
    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("contentType")]
    public string ContentType { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }
}
