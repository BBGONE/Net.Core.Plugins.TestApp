using System;
using System.Threading.Tasks;

namespace PluginContract
{
    public interface IPlugin: IDisposable
    {
        Task Configure(string jsonConfiguration, string configVersion);

        Task<string> HandleAsync(string input);

        void Cancel();

        string PluginName { get; }
    }
}
