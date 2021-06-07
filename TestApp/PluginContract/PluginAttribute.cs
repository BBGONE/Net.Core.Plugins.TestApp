using System;

namespace PluginContract
{
    /// <summary>
    ///  Аттрибут плагина
    /// </summary>
    [AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PluginAttribute : System.Attribute, IPluginAttribute
    {
        public PluginAttribute(string pluginName, string pluginVersion)
        {
            this.PluginName = pluginName;
            this.PluginVersion = pluginVersion;
        }

        /// <summary>
        /// Имя плагина
        /// </summary>
        public string PluginName
        {
            get;
        }

        /// <summary>
        /// Версия плагина
        /// </summary>
        public string PluginVersion
        {
            get;
        }
    }
}
