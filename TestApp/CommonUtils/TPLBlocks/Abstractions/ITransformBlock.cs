using System;

namespace CommonUtils.TPLBlocks
{
    public interface ITransformBlock<TInput, TOutput>: ISourceBlock<TOutput>, ITargetBlock<TInput>, IDisposable
    {
    }
}
