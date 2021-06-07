using System;
using System.Threading.Tasks;

namespace CommonUtils.TPLBlocks
{
    public interface ICallbackProxy<T>
    {
        BatchInfo BatchInfo { get; }
        Task TaskCompleted(T message, Exception error);
        bool JobCancelled();
        bool JobCompleted(Exception error);
    }
}
