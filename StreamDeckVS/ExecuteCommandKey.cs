using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using BarRaider.SdTools;
using EnvDTE;

namespace StreamDeckVS
{
    [PluginActionId("com.nicollasr.streamdeckvs.executecommand")]
    public class ExecuteCommandKey : Key<ExecuteCommandSettings>
    {
        public ExecuteCommandKey(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
        }

        public override void KeyPressed(KeyPayload payload)
        {
            base.KeyPressed(payload);

            try
            {
                (IntPtr processHandle, int processId) = Windows32API.GetForeground();

                if (IsProcessVisualStudio(processId))
                {
                    ExecuteCommand(DTEAPI.GetDTE(processId).FirstOrDefault(), settings);
                }
                else
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Foreground is not Visual Studio: {GetProcessName(processId)} ({processId})");

                    var instances = DTEAPI.GetDTE().ToList();

                    // 1. Foreground window belongs to the debugged process itself.
                    DTE dte = instances.FirstOrDefault(m => IsProcessAttachedToDebug(processId, m));

                    // 2. Console app started through VS debug console (pipe in command line).
                    if (dte is null)
                    {
                        string processCommandLine = GetProcessCommandLine(processId);

                        if (IsLinkedByPipe(processCommandLine))
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, "Foreground process is linked to Visual Studio by debug console pipe");
                            dte = DTEAPI.GetDTE(GetVisualStudioPIDFromPipeLink(processCommandLine)).FirstOrDefault();
                        }
                    }

                    // 3. Foreground window is owned by another process (Windows Terminal / conhost for console apps,
                    //    a browser for web projects, a launcher, ...): use the Visual Studio instance that is debugging.
                    if (dte is null)
                    {
                        dte = instances.FirstOrDefault(IsDebugging);

                        if (dte != null)
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, "Using the Visual Studio instance that is in debug mode");
                        }
                    }

                    if (dte is null)
                    {
                        Logger.Instance.LogMessage(TracingLevel.WARN, $"No Visual Studio instance for foreground process {processId}, command '{settings.Command}' skipped");
                    }

                    ExecuteCommand(dte, settings);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, ex.Message);
            }
        }

        private int GetVisualStudioPIDFromPipeLink(string link)
        {
            int indexLength = @"\\.\pipe\Microsoft-VisualStudio-Debug-Console-".Length;

            int start = link.IndexOf(@"\\.\pipe\Microsoft-VisualStudio-Debug-Console-");

            if (start >= 0)
            {
                int end = link.IndexOf(" ", start);

                if (end >= 0)
                {
                    if (int.TryParse(link.Substring(start + indexLength, end - start - indexLength), out int pid))
                    {
                        return pid;
                    }
                }
            }

            return 0;
        }

        private bool IsLinkedByPipe(string arguments) => arguments != null && arguments.Contains(@"\\.\pipe\Microsoft-VisualStudio-Debug-Console-");

        private void ExecuteCommand(DTE dte, ExecuteCommandSettings settings) => dte?.ExecuteCommand(settings.Command, settings.CommandArgs ?? "");

        private bool IsProcessVisualStudio(int processId)
        {
            return System.Diagnostics.Process.GetProcessById(processId)
                .ProcessName.Equals("devenv", StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool IsDebugging(DTE dte)
        {
            try
            {
                return dte.Debugger != null && dte.Debugger.CurrentMode != dbgDebugMode.dbgDesignMode;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Could not read debugger mode: {ex.Message}");
                return false;
            }
        }

        private static string GetProcessName(int processId)
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(processId).ProcessName;
            }
            catch
            {
                return "?";
            }
        }

        private static bool IsProcessAttachedToDebug(int processId, DTE dte)
        {
            if (dte.Debugger?.DebuggedProcesses?.Count > 0)
            {
                foreach (EnvDTE.Process debuggedProcess in dte.Debugger.DebuggedProcesses)
                {
                    if (debuggedProcess.ProcessID == processId)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Debugging process found, id: {processId}");

                        return true;
                    }
                }
            }

            return false;
        }

        private string GetProcessCommandLine(int processId)
        {
            try
            {
                ManagementObjectSearcher wmi = new ManagementObjectSearcher("root\\CIMV2", $"SELECT * FROM Win32_Process where ProcessId = {processId}");

                ManagementObject process = Enumerable.FirstOrDefault(wmi.Get() as IEnumerable<ManagementObject>);

                return (string)process?["CommandLine"];
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
