using Aws.Common.Models;
using Aws.Common.Models.EmailService;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace Aws.Common.Helpers;

public static class EmailParser
{
    public static EventModel GetEventDetails(string text)
    {
        // Define the Event object
        var eventObject = new EventModel();

        // Split the string into lines
        var lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        // Split each line by ':'
        foreach (var line in lines)
        {
            var indexOfColon = line.IndexOf(':');

            // No colon in the line - continue to next line.
            if (indexOfColon == -1)
                continue;

            string key = line.Substring(0, indexOfColon).Trim();
            string value = line.Substring(indexOfColon + 1).Trim();

            //Set Event object properties based on key value pairs
            switch (key.ToLower())
            {
                case "event_type":
                    eventObject.EventType = value;
                    break;

                case "object_key":
                    eventObject.ObjectKey = value;
                    break;

                case "object_type":
                    eventObject.ObjectType = value;
                    break;

                case "last_modified":
                    eventObject.LastModified = DateTime.Parse(value);
                    break;

                case "object_size":
                    eventObject.ObjectSize = long.Parse(value);
                    break;

                case "download_link":
                    eventObject.DownloadLink = value;
                    break;
            }
        }
        return eventObject;
    }

    public static string ExtractConfirmationURL(EmailMessageDetailedModel emailMessage)
    {
        if (string.IsNullOrEmpty(emailMessage.HtmlBody))
        {
            return ExtractConfirmationURLFromJson(emailMessage.Body);
        }
        else
        {
            return ExtractConfirmationURLFromHtml(emailMessage.HtmlBody);
        }
    }

    private static string ExtractConfirmationURLFromJson(string data)
    {
        var subscriptionConfirmation = JsonConvert.DeserializeObject<SubscriptionConfirmation>(data);

        return subscriptionConfirmation!.SubscribeURL;
    }

    private static string ExtractConfirmationURLFromHtml(string htmlString)
    {
        if (string.IsNullOrEmpty(htmlString))
        {
            throw new ArgumentException("HTML content is empty.");
        }

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(htmlString);

        var anchorTags = htmlDoc.DocumentNode.SelectNodes("//a");
        foreach (var anchorTag in anchorTags)
        {
            if (anchorTag.InnerText == "Confirm subscription")
                return anchorTag.GetAttributeValue("href", string.Empty);
        }

        return string.Empty;
    }
}
