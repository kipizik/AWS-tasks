using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Aws.Common.Models;
using Dapper;
using MySql.Data.MySqlClient;
using Renci.SshNet;
using System.Reflection;

namespace Aws.Common.Helpers;

public static class DbHelper
{
    public static async Task<List<Dictionary<string, string>>> GetDataFromSqlDbAsync(string ec2Host)
    {
        var ec2User = "ec2-user";
        var localhost = "127.0.0.1";
        var tableName = "images";

        var secretValues = await AwsSsmHelper.GetSecretAsync("DatabaseDBSecret");
        var dbConnectionInfo = new DbConnectionInfo
        {
            DbName = secretValues["dbname"],
            Password = secretValues["password"],
            UserName = secretValues["username"],
            Port = uint.Parse(secretValues["port"]),
            Server = secretValues["host"]
        };

        string keyDirectory = $"{Assembly.GetExecutingAssembly().Location}\\..\\PrivateKey";
        List<Dictionary<string, string>> queryResults;
        var connectionInfo = new ConnectionInfo(
            ec2Host,
            ec2User,
            new PrivateKeyAuthenticationMethod(ec2User, new PrivateKeyFile($"{keyDirectory}/autotest.pem")));

        using (var client = new SshClient(connectionInfo))
        {
            await client.ConnectAsync(CancellationToken.None);
            if (!client.IsConnected)
            {
                throw new Exception("SSH connection was not opened.");
            }

            var portForwarded = new ForwardedPortLocal(localhost, dbConnectionInfo.Port, dbConnectionInfo.Server, dbConnectionInfo.Port);
            client.AddForwardedPort(portForwarded);
            portForwarded.Start();

            var connectionString = $"server={localhost};database={dbConnectionInfo.DbName};uid={dbConnectionInfo.UserName};pwd={dbConnectionInfo.Password}";
            using (var conn = new MySqlConnection(connectionString))
            {
                await conn.OpenAsync();

                var queryResult = await conn.QueryAsync($"SELECT * FROM {tableName}");
                queryResults = queryResult.Select(x => (Dictionary<string, string>)x).ToList();
            }

            client.Disconnect();
        }
        return queryResults;
    }

    public static async Task<List<Dictionary<string, string>>> GetDataFromNoSqlDbAsync()
    {
        var client = new AmazonDynamoDBClient();
        var listTablesResponse = await client.ListTablesAsync();
        var tableName = listTablesResponse.TableNames.FirstOrDefault();
        if (string.IsNullOrEmpty(tableName))
        {
            throw new Exception("No DB tables were found.");
        }

        var scanRequest = new ScanRequest
        {
            TableName = tableName,
        };
        var scanResponse = await client.ScanAsync(scanRequest);
        var data = scanResponse.Items.Select(x => x.ToDictionary(x => x.Key, y => y.Value.N ?? y.Value.S)).ToList();

        return data;
    }
}
