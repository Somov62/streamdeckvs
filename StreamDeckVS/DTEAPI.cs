using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using BarRaider.SdTools;
using EnvDTE;

namespace StreamDeckVS
{
    public static class DTEAPI
    {
        private static readonly Regex _VsRegex = new Regex(@"^!VisualStudio\.DTE\.(?<major>\d+)\.\d+:(?<processId>\d+)$", RegexOptions.Compiled);
        public static IEnumerable<DTE> GetDTE(int? processId = null)
        {
            var dteInstances = new List<DTE>();
            IBindCtx bindCtx = null;
            IRunningObjectTable runningObjects = null;
            IEnumMoniker monikers = null;

            var foundByProcessId = false;

            try
            {
                Marshal.ThrowExceptionForHR(Windows32API.CreateBindCtx(0, out bindCtx));

                bindCtx.GetRunningObjectTable(out runningObjects);

                runningObjects.EnumRunning(out monikers);

                var moniker = new IMoniker[1];
                var fetchedMonikers = IntPtr.Zero;

                while (monikers.Next(1, moniker, fetchedMonikers) == 0)
                {
                    moniker[0].GetDisplayName(bindCtx, null, out var rotName);

                    var matches = _VsRegex.Matches(rotName);
                    if (matches.Count > 0)
                    {
                        /*
                         * The ROT name is in the format of !VisualStudio.DTE.{version}:{processId}.
                         * We can use this to filter out the DTE instances that are not for Visual Studio 2017 and later.
                         * We don't need to use int.TryParse here because the regex will only match valid integers, so we can safely use int.Parse.
                         */
                        var match = matches[0];
                        var version = int.Parse(match.Groups["major"].Value); // "18.0" -> 18; int.Parse("18.0") would throw FormatException
                        var rotProcessId = int.Parse(match.Groups["processId"].Value);

                        if (version < 15)
                        {
                            /*
                             * We only support Visual Studio 2017 and later (version 15.0+).
                             */
                            continue;
                        }

                        Marshal.ThrowExceptionForHR(runningObjects.GetObject(moniker[0], out var runningObject));

                        if (runningObject is DTE dte)
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, $"ROT Object Found {rotName}");

                            if (processId.HasValue && rotProcessId == processId)
                            {
                                foundByProcessId = true;

                                dteInstances.Clear();
                                dteInstances.Add(dte);

                                break;
                            }

                            dteInstances.Add(dte);
                        }
                    }
                }

                if (processId.HasValue && !foundByProcessId)
                {
                    return Enumerable.Empty<DTE>();
                }

                return dteInstances.AsEnumerable();
            }
            finally
            {
                if (monikers != null)
                {
                    Marshal.ReleaseComObject(monikers);
                }

                if (runningObjects != null)
                {
                    Marshal.ReleaseComObject(runningObjects);
                }

                if (bindCtx != null)
                {
                    Marshal.ReleaseComObject(bindCtx);
                }
            }
        }
    }
}
