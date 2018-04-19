﻿using System;
using System.Diagnostics;
using Milou.Deployer.Core.Extensions;

namespace Milou.Deployer.Core.Processes
{
    public static class ProcessExtensions
    {
        public static bool IsWin64(this Process process)
        {
#if !DOTNET5_4
            if ((Environment.OSVersion.Version.Major > 5)
                || ((Environment.OSVersion.Version.Major == 5) && (Environment.OSVersion.Version.Minor >= 1)))
            {
                IntPtr processHandle;
                bool retVal;

                try
                {
                    processHandle = Process.GetProcessById(process.Id).Handle;
                }
                catch (Exception ex)
                {
                    if (ex.IsFatal())
                    {
                        throw;
                    }

                    return false;
                }

                return NativeMethods.IsWow64Process(processHandle, out retVal) && retVal;
            }

#endif

            return false;
        }
    }
}