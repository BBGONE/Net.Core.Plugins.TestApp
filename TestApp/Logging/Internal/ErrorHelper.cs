using System;

namespace Extensions.Logging.RollingFile.Internal
{
	public static class ErrorHelper
	{
		public static string GetFullMessage(Exception exception, bool includeStackTrace = true)
		{
			string result = exception.GetType().Name + ": " + exception.Message;
            if (includeStackTrace)
            {
                result = result + Environment.NewLine + exception.StackTrace;
            }

			return result;
		}

        public static string ToFullMessageString(this Exception exception)
        {
            return GetFullMessage(exception, true);
        }
	}
}
