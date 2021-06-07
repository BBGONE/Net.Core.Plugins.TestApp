using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonUtils.Disposal;
using CommonUtils.TPLBlocks;

namespace CommonUtils.Extensions.TPLBlocks
{
    public static class TransformBlockExtensions
    {
        public static IDisposable LinkTo<TOutput>(this ISourceBlock<TOutput> inputBlock, ITargetBlock<TOutput> outputBlock)
        {
            Func<TOutput, Task> func = (async (output) => { await outputBlock.Post(output); });
            inputBlock.OutputSink += func;
            CancellationTokenSource cts = new CancellationTokenSource();

            inputBlock.Completion.ContinueWith((antecedent) => {
                outputBlock.Complete(antecedent.IsCanceled ? (Exception)(new OperationCanceledException()) : antecedent.Exception);
            }, cts.Token);

            return new AnonymousDisposable(()=> { cts.Cancel(); inputBlock.OutputSink -= func; });
        }

        public static IDisposable LinkWithPredicateTo<TOutput>(this ISourceBlock<TOutput> inputBlock, ITargetBlock<TOutput> outputBlock, Predicate<TOutput> predicate)
        {
            Func<TOutput, Task> func = (async (output) => {
                if (predicate(output))
                {
                    await outputBlock.Post(output);
                }
            });
            inputBlock.OutputSink += func;
            CancellationTokenSource cts = new CancellationTokenSource();

            inputBlock.Completion.ContinueWith((antecedent) => {
                outputBlock.Complete(antecedent.IsCanceled ? (Exception)(new OperationCanceledException()) : antecedent.Exception);
            }, cts.Token);

            return new AnonymousDisposable(() => { cts.Cancel(); inputBlock.OutputSink -= func; });
        }

        public static IDisposable LinkManyTo<TOutput>(this IEnumerable<ISourceBlock<TOutput>> inputBlocks, ITargetBlock<TOutput> outputBlock)
        {
            Func<TOutput, Task> func = (async (output) => { await outputBlock.Post(output); });

            foreach (var inputBlock in inputBlocks)
            {
                inputBlock.OutputSink += func;
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            var inputBlocksCompletions = inputBlocks.Select(ib => ib.Completion).ToArray();

            var completeTask = Task.WhenAll(inputBlocksCompletions);

            completeTask.ContinueWith((antecedent) => {
                outputBlock.Complete(antecedent.IsCanceled ? (Exception)(new OperationCanceledException()) : antecedent.Exception);
            }, cts.Token);

            return new AnonymousDisposable(() => { cts.Cancel(); foreach(var block in inputBlocks) block.OutputSink -= func; });
        }

        public static ITransformBlock<TOutput, TResult> LinkTo<TOutput, TResult>(this ISourceBlock<TOutput> inputBlock, ITransformBlock<TOutput, TResult> outputBlock)
        {
            inputBlock.LinkTo<TOutput>(outputBlock);
            return outputBlock;
        }

        public static ITransformBlock<TOutput, TResult> LinkWithPredicateTo<TOutput, TResult>(this ISourceBlock<TOutput> inputBlock, ITransformBlock<TOutput, TResult> outputBlock, Predicate<TOutput> predicate)
        {
            inputBlock.LinkWithPredicateTo<TOutput>(outputBlock, predicate);
            return outputBlock;
        }

        public static ITransformBlock<TOutput, TResult> LinkManyTo<TOutput, TResult>(this IEnumerable<ISourceBlock<TOutput>> inputBlocks, ITransformBlock<TOutput, TResult> outputBlock)
        {
            inputBlocks.LinkManyTo<TOutput>(outputBlock);
            return outputBlock;
        }
    }
}
