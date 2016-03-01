﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MinBuild
{
    public class CheckCompileCacheCPP : CacheTaskCPP
    {
        [Output]
        public bool RestoreSuccessful { get; private set; }

        public override bool Execute()
        {
            LogProjectMessage("Recompile requested: " + BuildConfig);

            var tlogCacheLocation = GetTLogCacheLocation();
            if (string.IsNullOrWhiteSpace(tlogCacheLocation)) return true;
            var completeMarker = Path.Combine(tlogCacheLocation, "complete");
            if (!File.Exists(completeMarker))
            {
                LogProjectMessage(completeMarker + " is missing, recompiling...");
                return true;
            }

            // Reset deletion timer on tlog cache directory
            File.SetLastWriteTimeUtc(completeMarker, DateTime.UtcNow);

            EvaluateLinkTLogFilename(tlogCacheLocation);
            IList<string> realInputs, realOutputs;
            try
            {
                if (!ParseRealInputsAndOutputs(tlogCacheLocation, out realInputs, out realOutputs, true))
                    return true;
            }
            catch (Exception)
            {
                LogProjectMessage("Ignoring existing tlog with absolute paths");
                return true;
            }

            bool restoreSuccessful;
            RestoreCachedArtifactsIfPossible(realInputs, realOutputs, out restoreSuccessful);
            RestoreSuccessful = restoreSuccessful;

            return true;
        }
    }
}
