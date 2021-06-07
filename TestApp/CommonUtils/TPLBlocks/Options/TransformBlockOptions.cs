using System;
using System.Threading;
using System.Threading.Tasks;

namespace CommonUtils.TPLBlocks.Options
{
    public class TransformBlockOptions
    {
        public static readonly TransformBlockOptions Default = new TransformBlockOptions();

        public TransformBlockOptions()
        {
            BoundedCapacity = 100;
            MaxDegreeOfParallelism = Environment.ProcessorCount;
            this.TaskScheduler = TaskScheduler.Default;
        }

        public int? BoundedCapacity { get; set; }

        public int MaxDegreeOfParallelism { get; set; }

        public CancellationToken? CancellationToken { get; set; }

        public TaskScheduler TaskScheduler {get; set;}
    }

}
