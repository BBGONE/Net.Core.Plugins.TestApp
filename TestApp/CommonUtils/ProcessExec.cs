using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommonUtils
{
    public class DataReceivedEventArgs : EventArgs
    {
        public string Text { get; }

        public DataReceivedEventArgs(string text)
        {
            Text = text;
        }
    }

    public class AlreadyRunningException : System.ApplicationException
    {
        public AlreadyRunningException() : base("Программа уже исполняется")
        { 
        }
    }

    public class ProcessExec
    {
        private Process process;
        private Encoding stdEncoding;
        private CancellationTokenSource cancellationTokenSource;
        private TaskCompletionSource<string> tcs;
        private int IsRunningFlag;
        private StringBuilder resultBuilder;
        private StringBuilder errorBuilder;
        private Task task1;
        private Task task2;

        
        public event EventHandler<DataReceivedEventArgs> StdOutReceived;
        public event EventHandler<DataReceivedEventArgs> StdErrReceived;


        public string FileName;
        public string Arguments;
        public string WorkingDirectory;

        public ProcessExec(Encoding encoding = null)
        {
            stdEncoding = encoding ?? Encoding.GetEncoding(866);
        }

        #region Internal
        private Task<string> InternalStart()
        {
            tcs = new TaskCompletionSource<string>();
            cancellationTokenSource = new CancellationTokenSource();
            resultBuilder = new StringBuilder();
            errorBuilder = new StringBuilder();

            try
            {
                var cancellationToken = cancellationTokenSource.Token;

                StartProcess(cancellationToken);

                CancellationTokenRegistration reg = cancellationToken.Register(() => {
                    if (Interlocked.CompareExchange(ref IsRunningFlag, 0, 1) == 1 && tcs.TrySetCanceled())
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch(Exception ex)
                        {
                            errorBuilder.Append(ex.Message);
                        }
                    }
                });

                tcs.Task.ContinueWith((antecedent) => { var err = antecedent.Exception; reg.Dispose(); });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }


            return tcs.Task;
        }

        protected virtual void StartProcess(CancellationToken cancellationToken)
        {
            process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = false;
            process.StartInfo.FileName = FileName;
            process.StartInfo.Arguments = Arguments;
            process.StartInfo.StandardOutputEncoding = this.stdEncoding;
            process.StartInfo.StandardErrorEncoding = this.stdEncoding;
            process.StartInfo.WorkingDirectory = WorkingDirectory;
            process.EnableRaisingEvents = true;

            process.Exited += (s, a) => {
                int res = process.ExitCode;

                try
                {
                    Task.WaitAll(new Task[] { task1 ?? Task.CompletedTask, task2 ?? Task.CompletedTask }, cancellationToken);

                    this.OnComplete(res, null);
                }
                catch (Exception ex)
                {
                    this.OnComplete(res, ex);
                }
            };

            if (!process.Start())
            {
                tcs.TrySetException(new Exception("Процесс не запустился"));
                return;
            }

            task1 = Task.Factory.StartNew(ReadStdOut, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
            task2 = Task.Factory.StartNew(ReadStdErr, cancellationToken, TaskCreationOptions.None, TaskScheduler.Default).Unwrap();
        }

        private void OnComplete(int res, Exception ex)
        {
            if (errorBuilder.Length > 0)
            {
                tcs.TrySetException(new Exception(errorBuilder.ToString()));
            }
            else if (ex != null)
            {
                tcs.TrySetException(ex);
            }
            else if (res != 0)
            {
                tcs.TrySetException(new Exception($"Процесс {FileName} завершился с ошибкой: {Environment.NewLine}{resultBuilder.ToString()}"));
            }
            else
            {
                tcs.TrySetResult(resultBuilder.ToString());
            }


            Interlocked.CompareExchange(ref IsRunningFlag, 0, 1);
        }

        protected virtual async Task ReadStdOut()
        {
            var cancellationToken = this.cancellationTokenSource.Token;
            string str;
            StreamReader reader = process.StandardOutput;
            while ((str = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resultBuilder.AppendLine(str);
                StdOutReceived?.Invoke(this, new DataReceivedEventArgs(str));
            }
        }

        protected virtual async Task ReadStdErr()
        {
            var cancellationToken = this.cancellationTokenSource.Token;
            string str;
            StreamReader reader = process.StandardError;
            while ((str = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                errorBuilder.AppendLine(str);
                StdErrReceived?.Invoke(this, new DataReceivedEventArgs(str));
            }
        }
        #endregion

        public Task<string> Start()
        {
            if (Interlocked.CompareExchange(ref IsRunningFlag, 1, 0) == 1)
            {
                throw new AlreadyRunningException();
            }

            return InternalStart();
        }

        public void Cancel()
        {
            if (IsRunningFlag == 1)
            {
                cancellationTokenSource.Cancel();
            }
        }
    }
}
