using System.Threading.Tasks;

namespace CommonUtils.TPLBlocks
{
    public interface ITargetBlock<TInput>: IDataflowBlock
    {
        ValueTask<bool> Post(TInput msg);
    }

}
