using Newtonsoft.Json;

namespace Aws.Common.Models;

public class ImageModel
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("last_modified")]
    public string LastModified { get; set; }

    [JsonProperty("object_key")]
    public string ObjectKey { get; set; }

    [JsonProperty("object_size")]
    public long ObjectSize { get; set; }

    [JsonProperty("object_type")]
    public string ObjectType { get; set; }
}

