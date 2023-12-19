namespace Aws.Iam.Task2.Tests.Helpers;

public static class CredentialsManager
{
    public static string GetAccessKeyId()
    {
        return GetEnvVariableValue("Aws_Access_Key_Id");
    }

    public static string GetSecretAccessKey()
    {
        return GetEnvVariableValue("Aws_Secret_Access_Key");
    }

    private static string GetEnvVariableValue(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) 
            ?? throw new Exception($"Value for the env variable was not found. Name is {name}");
    }
}
