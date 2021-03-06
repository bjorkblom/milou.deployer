﻿using System.Collections.Immutable;
using System.Threading.Tasks;
using Arbor.Tooler;
using Milou.Deployer.Bootstrapper.Common;

namespace Milou.Deployer.Bootstrapper
{
    public static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            int exitCode;

            using (App app = await App.CreateAsync(args).ConfigureAwait(false))
            {
                NuGetPackageInstallResult nuGetPackageInstallResult = await app.ExecuteAsync(args.ToImmutableArray()).ConfigureAwait(false);

                exitCode = nuGetPackageInstallResult.SemanticVersion is {} && nuGetPackageInstallResult.PackageDirectory is {} ? 0 : 1;
            }

            return exitCode;
        }
    }
}
