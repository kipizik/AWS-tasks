namespace Aws.Common.Models;

public class DbConnectionInfo
{
    public string UserName { get; set; }
    public string Password { get; set; }
    public string DbName { get; set; }
    public uint Port { get; set; }
    public string Server { get; set; }
}
