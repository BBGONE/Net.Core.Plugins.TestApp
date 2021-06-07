using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommonUtils
{
    public static class Retry
    {
        public static async Task Do(
            Action action,
            TimeSpan retryInterval,
            int maxAttemptCount = 3)
        {
            await Do<object>(() =>
            {
                action();
                return null;
            }, retryInterval, maxAttemptCount);
        }

        public static async Task<T> Do<T>(
            Func<T> action,
            TimeSpan retryInterval,
            int maxAttemptCount = 3)
        {
            var exceptions = new List<Exception>();

            for (int attempted = 0; attempted < maxAttemptCount; attempted++)
            {
                try
                {
                    if (attempted > 0)
                    {
                        await Task.Delay(retryInterval);
                    }
                    return action();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
            throw new AggregateException(exceptions);
        }
    }
}
