using Microsoft.Extensions.DependencyInjection;
using Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;
using Mihon.ExtensionsBridge.IKVMCompiler.Services;

namespace Mihon.ExtensionsBridge.IKVMCompiler
{
    public static class Extensions
    {
        public static IServiceCollection AddIKVMCompiler(this IServiceCollection services)
        {
            services.AddSingleton<ICompilerWorkingFolderStructure, CompilerWorkingFolderStructure>();
            services.AddSingleton<IIKVMVersion, IKVMVersion>(a =>
            {
                return new IKVMVersion("8.15.0", "net10.0", "net8.0");
            });
            services.AddSingleton<IIkvmCompiler, IkvmCompiler>();
            services.AddScoped<IIkvmCompilerDownloader, IkvmCompilerDownloader>();
            services.AddHttpClient(nameof(IkvmCompilerDownloader));
            return services;
        }
    }
}
