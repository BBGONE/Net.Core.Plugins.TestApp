using System;
using System.Threading.Tasks;
using CommonUtils.Errors;
using PluginContract;

namespace CommonUtils.TPLBlocks
{
    class TCallBack<TMsg> : BaseCallback<TMsg>
    {
        private readonly IPluginLogger _logger;

        public TCallBack(IPluginLogger logger)
        {
            _logger = logger;
        }

        public override void TaskSuccess(TMsg message)
        {
            // NOOP
        }
        public override async Task<bool> TaskError(TMsg message, Exception error)
        {
            await Task.CompletedTask;
            _logger.Log(LogLevel.Error, 1, ErrorHelper.GetFullMessage(error));
            return false;
        }
    }
}
