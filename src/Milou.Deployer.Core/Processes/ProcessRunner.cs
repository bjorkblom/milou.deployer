using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Milou.Deployer.Core.Extensions;
using Serilog;

namespace Milou.Deployer.Core.Processes
{
    public static class ProcessRunner
    {
        public static Task<ExitCode> ExecuteAsync(string executePath,
            IEnumerable<string> arguments = null, ILogger logger = null,
            IEnumerable<KeyValuePair<string, string>> environmentVariables = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteAsync(executePath, cancellationToken, arguments, standardOutLog: logger.Information,
                standardErrorAction: logger.Error,
                verboseAction: logger.Verbose, toolAction: logger.Information,
                environmentVariables: environmentVariables,
                debugAction: logger.Debug);
        }

        public static async Task<ExitCode> ExecuteAsync(string executePath,
            CancellationToken cancellationToken = default(CancellationToken),
            IEnumerable<string> arguments = null,
            Action<string, string> standardOutLog = null,
            Action<string, string> standardErrorAction = null,
            Action<string, string> toolAction = null,
            Action<string, string> verboseAction = null,
            IEnumerable<KeyValuePair<string, string>> environmentVariables = null,
            Action<string, string> debugAction = null)
        {
            if (string.IsNullOrWhiteSpace(executePath))
            {
                throw new ArgumentNullException(nameof(executePath));
            }

            if (!File.Exists(executePath))
            {
                throw new ArgumentException(
                    $"The executable file '{executePath}' does not exist",
                    nameof(executePath));
            }

            IEnumerable<string> usedArguments = arguments ?? Enumerable.Empty<string>();

            string formattedArguments = string.Join(" ", usedArguments.Select(arg => $"\"{arg}\""));

            Task<ExitCode> task = RunProcessAsync(executePath, formattedArguments, standardErrorAction, standardOutLog,
                cancellationToken, toolAction, verboseAction, environmentVariables, debugAction);

            ExitCode exitCode = await task;

            return exitCode;
        }

        private static async Task<ExitCode> RunProcessAsync(string executePath, string formattedArguments,
            Action<string, string> standardErrorAction, Action<string, string> standardOutputLog,
            CancellationToken cancellationToken, Action<string, string> toolAction,
            Action<string, string> verboseAction = null,
            IEnumerable<KeyValuePair<string, string>> environmentVariables = null,
            Action<string, string> debugAction = null)
        {
            Action<string, string> usedToolAction = toolAction ?? ((_, __) => { });
            Action<string, string> standardAction = standardOutputLog ?? ((_, __) => { });
            Action<string, string> errorAction = standardErrorAction ?? ((_, __) => { });
            Action<string, string> verbose = verboseAction ?? ((_, __) => { });
            Action<string, string> debug = debugAction ?? ((_, __) => { });

            var taskCompletionSource = new TaskCompletionSource<ExitCode>();

            string processWithArgs = $"\"{executePath}\" {formattedArguments}".Trim();

            usedToolAction($"[{typeof(ProcessRunner).Name}] Executing: {processWithArgs}", null);

            bool useShellExecute = standardErrorAction == null && standardOutputLog == null;

            bool redirectStandardError = standardErrorAction != null;

            bool redirectStandardOutput = standardOutputLog != null;

            var processStartInfo = new ProcessStartInfo(executePath)
            {
                Arguments = formattedArguments,
                RedirectStandardError = redirectStandardError,
                RedirectStandardOutput = redirectStandardOutput,
                UseShellExecute = useShellExecute
            };

#if !DOTNET5_4

            if (environmentVariables != null)
            {
                foreach (KeyValuePair<string, string> environmentVariable in environmentVariables)
                {
                    processStartInfo.EnvironmentVariables.Add(environmentVariable.Key, environmentVariable.Value);
                }
            }

#endif

            var exitCode = new ExitCode(-1);

            var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

#if !DOTNET5_4
            process.Disposed += (_, __) =>
            {
                if (!taskCompletionSource.Task.IsCompleted)
                {
                    verbose("Task was not completed, but process was disposed", null);
                    taskCompletionSource.TrySetResult(ExitCode.Failure);
                }
                verbose($"Disposed process '{processWithArgs}'", null);
            };

#endif

            if (redirectStandardError)
            {
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                    {
                        errorAction(args.Data, null);
                    }
                };
            }
            if (redirectStandardOutput)
            {
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                    {
                        standardAction(args.Data, null);
                    }
                };
            }

            process.Exited += (sender, _) =>
            {
                var proc = (Process) sender;
                toolAction?.Invoke($"Process '{processWithArgs}' exited with code {new ExitCode(proc.ExitCode)}", null);
                taskCompletionSource.SetResult(new ExitCode(proc.ExitCode));
            };

            int processId = -1;

            try
            {
                bool started = process.Start();

                if (!started)
                {
                    errorAction($"Process '{processWithArgs}' could not be started", null);
                    return ExitCode.Failure;
                }

                if (redirectStandardError)
                {
                    process.BeginErrorReadLine();
                }

                if (redirectStandardOutput)
                {
                    process.BeginOutputReadLine();
                }

                int bits = process.IsWin64() ? 64 : 32;

                try
                {
                    processId = process.Id;
                }
                catch (InvalidOperationException ex)
                {
                    debug($"Could not get process id for process '{processWithArgs}'. {ex}", null);
                }

                string temp = process.HasExited ? "was" : "is";

                verbose(
                    $"The process '{processWithArgs}' {temp} running in {bits}-bit mode",
                    null);
            }
            catch (Exception ex)
            {
                if (ex.IsFatal())
                {
                    throw;
                }
                errorAction($"An error occured while running process {processWithArgs}: {ex}", null);
                taskCompletionSource.SetException(ex);
            }
            bool done = false;
            try
            {
                while (IsAlive(process, taskCompletionSource.Task, cancellationToken, done, processWithArgs, verbose))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    Task delay = Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                    await delay;

                    if (taskCompletionSource.Task.IsCompleted)
                    {
                        done = true;
                        exitCode = await taskCompletionSource.Task;
                    }
                    else if (taskCompletionSource.Task.IsCanceled)
                    {
                        exitCode = ExitCode.Failure;
                    }
                    else if (taskCompletionSource.Task.IsFaulted)
                    {
                        exitCode = ExitCode.Failure;
                    }
                    else
                    {
                        exitCode = ExitCode.Failure;
                    }
                }
            }
            finally
            {
#if !DOTNET5_4
                if (!exitCode.IsSuccess)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        if (process != null && !process.HasExited)
                        {
                            try
                            {
                                toolAction($"Cancellation is requested, trying to kill process {processWithArgs}", null);

                                if (processId > 0)
                                {
                                    string args = $"/PID {processId}";
                                    string killProcessPath =
                                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                            "taskkill.exe");
                                    toolAction($"Running {killProcessPath} {args}", null);
                                    Process.Start(killProcessPath, args);

                                    errorAction(
                                        $"Killed process {processWithArgs} because cancellation was requested", null);
                                }
                                else
                                {
                                    debugAction(
                                        $"Could not kill process '{processWithArgs}', missing process id",
                                        null);
                                }
                            }
                            catch (Exception ex)
                            {
                                if (ex.IsFatal())
                                {
                                    throw;
                                }

                                errorAction(
                                    $"ProcessRunner could not kill process {processWithArgs} when cancellation was requested",
                                    null);
                                errorAction(
                                    $"Could not kill process {processWithArgs} when cancellation was requested", null);
                                errorAction(ex.ToString(), null);
                            }
                        }
                    }
                }
#endif
                using (process)
                {
                    verbose(
                        $"Task status: {taskCompletionSource.Task.Status}, {taskCompletionSource.Task.IsCompleted}",
                        null);
                    verbose($"Disposing process {processWithArgs}", null);
                }
            }

            verbose($"Process runner exit code {exitCode} for process {processWithArgs}", null);

            try
            {
                if (processId > 0)
                {
                    Process stillRunningProcess = Process.GetProcesses().SingleOrDefault(p => p.Id == processId);

                    if (stillRunningProcess != null)
                    {
                        if (!stillRunningProcess.HasExited)
                        {
                            errorAction(
                                $"The process with ID {processId.ToString(CultureInfo.InvariantCulture)} '{processWithArgs}' is still running",
                                null);

                            return ExitCode.Failure;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.IsFatal())
                {
                    throw;
                }
                debugAction($"Could not check processes. {ex}", null);
            }

            return exitCode;
        }

        private static bool IsAlive(Process process, Task<ExitCode> task, CancellationToken cancellationToken, bool done,
            string processWithArgs, Action<string, string> verbose)
        {
            if (process == null)
            {
                verbose($"Process {processWithArgs} does no longer exist", null);
                return false;
            }

            if (task.IsCompleted || task.IsFaulted || task.IsCanceled)
            {
                TaskStatus status = task.Status;
                verbose($"Task status for process {processWithArgs} is {status}", null);
                return false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                verbose($"Cancellation is requested for process {processWithArgs}", null);
                return false;
            }

            if (done)
            {
                verbose($"Process {processWithArgs} is flagged as done", null);
                return false;
            }

            return true;
        }
    }
}
