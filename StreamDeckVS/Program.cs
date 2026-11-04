using System;
using BarRaider.SdTools;
using NLog.Filters;
using LogManager = NLog.LogManager;

namespace StreamDeckVS
{
    internal class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            SuppressTitleParserNoise();
            SDWrapper.Run(args);
        }

        /// <summary>
        /// fifine Control Deck / StreamDock sends its own title font (e.g. "HarmonyOS Sans") which is not
        /// installed in Windows, so BarRaider's TitleParameters logs an ERROR with a stack trace on every
        /// title update. It is harmless; drop those entries from the log.
        /// BarRaider.SdTools.Logger configures NLog in code (an NLog.config file would be overwritten),
        /// so we add the filter to its rules after it has initialized.
        /// </summary>
        private static void SuppressTitleParserNoise()
        {
            try
            {
                _ = Logger.Instance; // forces BarRaider to build its NLog configuration

                var config = LogManager.Configuration;
                if (config == null)
                {
                    return;
                }

                foreach (var rule in config.LoggingRules)
                {
                    // NLog 5: a rule with filters ignores everything not matched unless told otherwise.
                    rule.FilterDefaultAction = FilterResult.Log;
                    rule.Filters.Add(new WhenContainsFilter
                    {
                        Layout = "${message}",
                        // 3.2.0: "TitleParser failed to parse payload ...", 7.0.0: "TitleParameters failed to parse payload ..."
                        Substring = "failed to parse payload",
                        Action = FilterResult.Ignore
                    });
                }

                LogManager.ReconfigExistingLoggers();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Could not install TitleParser log filter: {ex.Message}");
            }
        }
    }
}
