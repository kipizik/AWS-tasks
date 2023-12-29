using Newtonsoft.Json;

namespace AWs.S3.Task5.Tests.Models;

internal class ImageModel
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("last_modified")]
    public string LastModified { get; set; }

    [JsonProperty("object_key")]
    public string ObjectKey { get; set; }

    [JsonProperty("object_size")]
    public int ObjectSize { get; set; }

    [JsonProperty("object_type")]
    public string ObjectType { get; set; }
}

