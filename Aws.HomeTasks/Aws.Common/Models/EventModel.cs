namespace Aws.Common.Models;

public class EventModel
{
    public string EventType { get; set; }
    public string ObjectKey { get; set; }
    public string ObjectType { get; set; }
    public string LastModified { get; set; }
    public long ObjectSize { get; set; }
    public string DownloadLink { get; set; }
}
