using Aws.Common.Models;
using Dapper;
using MySql.Data.MySqlClient;
using Renci.SshNet;
using System.Reflection;

namespace Aws.Rds.Task6.Tests.Helpers;

internal static class DbHelper
{
    public static async Task<List<IDictionary<string, object>>> GetDataFromDbAsync(string ec2Host, string ec2User, DbConnectionInfo dbConnectionInfo, string tableName)
    {
        var localhost = "127.0.0.1";
        string keyDirectory = $"{Assembly.GetExecutingAssembly().Location}\\..\\PrivateKey";
        List<IDictionary<string, object>> queryResults;
        var connectionInfo = new ConnectionInfo(
            ec2Host,
            ec2User,
            new PrivateKeyAuthenticationMethod(ec2User, new PrivateKeyFile($"{keyDirectory}/autotest.pem")));

        using (var client = new SshClient(connectionInfo))
        {
            try
            {
                await client.ConnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                throw;
            }
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
                queryResults = queryResult.Select(x => (IDictionary<string, object>)x).ToList();
            }

            client.Disconnect();
        }
        return queryResults;
    }
}
