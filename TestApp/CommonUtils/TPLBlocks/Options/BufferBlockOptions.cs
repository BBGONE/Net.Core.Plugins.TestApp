using System.Threading;
using System.Threading.Tasks;

namespace CommonUtils.TPLBlocks.Options
{
    public class BufferBlockOptions
    {
        public static readonly BufferBlockOptions Default = new BufferBlockOptions();

        public BufferBlockOptions()
        {
            BoundedCapacity = 100;
            this.TaskScheduler = TaskScheduler.Default;
        }

        public int? BoundedCapacity { get; set; }

        public CancellationToken? CancellationToken { get; set; }

        public TaskScheduler TaskScheduler { get; set; }
    }

}
