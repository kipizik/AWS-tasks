namespace Aws.Common.Helpers;

public static class Waiter
{
    public static async Task WaitForAsync(Func<Task<bool>> action, TimeSpan retryDelay, TimeSpan timeout)
    {
        TimeSpan time = TimeSpan.FromSeconds(0);
        while (time < timeout) 
        {
            if (await action())
            {
                break;
            }
            await Task.Delay(retryDelay);
            time += retryDelay; 
        }
    }
}
