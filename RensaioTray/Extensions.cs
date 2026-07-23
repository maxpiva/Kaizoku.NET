using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mihon.ExtensionsBridge.Models.Abstractions;
using RensaioTray.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RensaioTray
{
    public static class Extensions
    {
        public static IHostBuilder AddCefTimer(this IHostBuilder builder)
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartCefTimer, CefTimer>());
            return builder;
        }
    }
}
