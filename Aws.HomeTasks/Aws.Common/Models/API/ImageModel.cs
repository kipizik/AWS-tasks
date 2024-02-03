using Newtonsoft.Json;

namespace Aws.Common.Models.API;

public class ImageModel
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("last_modified")]
    public string LastModified { get; set; }

    [JsonProperty("object_key")]
    public string ObjectKey { get; set; }

    [JsonProperty("object_size")]
    public string ObjectSize { get; set; }

    [JsonProperty("object_type")]
    public string ObjectType { get; set; }

    [JsonProperty("created_at")]
    public string CreatedAt { get; set; }
}

