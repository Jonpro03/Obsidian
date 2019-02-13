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
        Server OriginServer;

        internal PluginManager(Server server)
        {
            this.Plugins = new ConcurrentHashSet<Plugin>();
            this.OriginServer = server;
        }

        internal async Task LoadPluginsAsync(Logger logger)
        {
            if (!Directory.Exists("plugins"))
            {
                Directory.CreateDirectory("plugins");
            }
            var files = Directory.GetFiles("plugins", "*.dll");
             // I don't do File IO often, I just know how to do reflection from a dll
            foreach (var file in files) // don't touch pls
            {
                var assembly = Assembly.LoadFile(Path.Combine(Path.GetFullPath("plugins"), Path.GetFileName(file)));
                var pluginclasses = assembly.GetTypes().Where(x => typeof(IPluginClass).IsAssignableFrom(x) && x != typeof(IPluginClass));

                foreach(var ptype in pluginclasses)
                {
                    var pluginclass = (IPluginClass)Activator.CreateInstance(ptype);
                    var plugininfo = pluginclass.Initialize(OriginServer);
                    var plugin = new Plugin(plugininfo, pluginclass);

                    Plugins.Add(plugin);
                    await logger.LogMessageAsync($"Loaded plugin: {plugininfo.Name} by {plugininfo.Author}");
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