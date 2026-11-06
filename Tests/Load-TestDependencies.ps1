# O host PowerShell nao aplica os bindingRedirects do Web.config do ASP.NET.
if (-not ('DreamMover.TestHost.Dependencies' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
namespace DreamMover.TestHost {
    public static class Dependencies {
        private static string directory;
        public static void Register(string binDirectory) {
            directory = binDirectory;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }
        private static Assembly Resolve(object sender, ResolveEventArgs args) {
            string name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources", StringComparison.Ordinal)) return null;
            string candidate = Path.Combine(directory, name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
    }
}
'@
    [DreamMover.TestHost.Dependencies]::Register((Join-Path (Split-Path -Parent $PSScriptRoot) 'bin'))
}
