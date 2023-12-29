using Newtonsoft.Json;

namespace Aws.Common.Models;

public class PolicyVersionModel
{
    [JsonIgnore]
    public string Version { get; set; } = default!;
    public Statement[] Statement { get; set; } = default!;
}

public class Statement
{
    public object Action { get; set; } = default!;
    public object Resource { get; set; } = default!;
    public string Effect { get; set; } = default!;
}
