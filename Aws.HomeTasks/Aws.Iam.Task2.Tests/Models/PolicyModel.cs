using Newtonsoft.Json;

namespace Aws.Iam.Task2.Tests.Models;

internal class PolicyModel
{
    [JsonIgnore]
    public string Version { get; set; } = default!;
    public Statement[] Statement { get; set; } = default!;
}

internal class Statement
{
    public object Action { get; set; } = default!;
    public string Resource { get; set; } = default!;
    public string Effect { get; set; } = default!;
}
