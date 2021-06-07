using System;
using System.Threading.Tasks;

namespace CommonUtils.TPLBlocks
{
    public interface ISourceBlock<TOutput>: IDataflowBlock
    {
        event Func<TOutput, Task> OutputSink;
    }
}
