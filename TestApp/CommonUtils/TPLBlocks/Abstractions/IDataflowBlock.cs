using System;
using System.Threading.Tasks;

namespace CommonUtils.TPLBlocks
{
    public interface IDataflowBlock:  IDisposable
    {
        BatchInfo BatchInfo { get; }

        Task Completion { get; }

        long Complete(Exception exception = null);
    }
}
