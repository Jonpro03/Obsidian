﻿using Obsidian.Concurrency;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using System.Linq;
using Obsidian.Logging;
using System.Threading.Tasks;

namespace Obsidian.Plugins
{
    public class PluginManager
    {
        public ConcurrentHashSet<Plugin> Plugins { get; private set; }
        private readonly Server Server;
        private string Path => System.IO.Path.Combine(Server.Path, "plugins");

        internal PluginManager(Server server)
        {
            this.Plugins = new ConcurrentHashSet<Plugin>();
            this.Server = server;
        }

        internal async Task LoadPluginsAsync(Logger logger)
        {
            if (!Directory.Exists(Path))
            {
                Directory.CreateDirectory(Path);
            }

            string[] files = Directory.GetFiles(Path, "*.dll");
            // I don't do File IO often, I just know how to do reflection from a dll
            foreach (var file in files) // don't touch pls
            {
                var assembly = Assembly.LoadFile(file);
                var pluginclasses = assembly.GetTypes().Where(x => typeof(IPluginClass).IsAssignableFrom(x) && x != typeof(IPluginClass));

                foreach (var ptype in pluginclasses)
                {
                    var pluginClass = (IPluginClass)Activator.CreateInstance(ptype);
                    var pluginInfo = await pluginClass.InitializeAsync(Server);
                    var plugin = new Plugin(pluginInfo, pluginClass);

                    Plugins.Add(plugin);
                    logger.LogMessage($"Loaded plugin: {pluginInfo.Name} by {pluginInfo.Author}");
                }
            }
        }
    }

    public struct Plugin
    {
        public PluginInfo Info { get; }
        public IPluginClass Class { get; }

        public Plugin(PluginInfo info, IPluginClass pclass)
        {
            this.Info = info;
            this.Class = pclass;
        }
    }
}