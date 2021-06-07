using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommonUtils.Extensions.TPLBlocks;
using CommonUtils.TPLBlocks;
using CommonUtils.TPLBlocks.Options;
using PluginContract;

namespace TransformBlockTest
{
    public class Executor
    {
        private readonly IPluginLoggerFactory loggerFactory;

        public Executor(IPluginLoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
        }

        public async Task DoWork(byte[] data)
        {
            await ExecuteFilesBlock(data);
        }

        private ITransformBlock<TInput, TOutput> CreateBlock<TInput, TOutput>(
            Func<TInput, Task<TOutput>> body = null,
            Func<TOutput, Task> outputSink = null,
            CancellationToken? token = null,
            int maxDegreeOfParallelism = 4)
        {

            ITransformBlock<TInput, TOutput> block = new TransformBlock<TInput, TOutput>(body, this.loggerFactory, new TransformBlockOptions() { CancellationToken = token, TaskScheduler = TaskScheduler.Default, MaxDegreeOfParallelism = maxDegreeOfParallelism });


            if (outputSink != null)
            {
                block.OutputSink += outputSink;
            }

            return block;
        }

        private async Task ExecuteFilesBlock(byte[] inputData, CancellationToken? token = null)
        {
            int processedCount = 0;


            const int BATCH_SIZE = 5;

            using (ITransformBlock<byte[], string> inputBlock = CreateBlock<byte[], string>(token: token, body: async (data) => {
                string filePath = Path.GetTempFileName();
                await File.WriteAllBytesAsync(filePath, data);
                return filePath;
            }, maxDegreeOfParallelism: 1))
            using (ITransformBlock<string, byte[]> outputBlock = CreateBlock<string, byte[]>(token: token, body: async (filePath) => {
                try
                {
                    var result = await File.ReadAllBytesAsync(filePath);
                    return result;
                }
                finally
                {
                    File.Delete(filePath);
                }
            }, maxDegreeOfParallelism: 1))
            {
                var lastBlock = inputBlock.LinkTo(outputBlock);

                lastBlock.OutputSink += (outputData) => {
                    Interlocked.Increment(ref processedCount);
                    return Task.CompletedTask;
                };

                var t1 = Task.Run(async () =>
                {
                    for (int i = 0; i < BATCH_SIZE; ++i)
                    {
                        await inputBlock.Post(inputData);
                    }

                    inputBlock.Complete();
                });


                await lastBlock.Completion;
            }
        }
    }
}
