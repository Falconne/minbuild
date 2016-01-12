using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CacheArtifactsCPP : CacheTaskCPP
    {
        [Required]
        public string LocalTlogLocation { protected get; set; }

        public override bool Execute()
        {
            LogProjectMessage("Caching after link, if tracking logs found");

            var tlogCacheLocation = GetTLogCacheLocation();
            if (string.IsNullOrWhiteSpace(tlogCacheLocation))
                return true;

            // Check that tracking logs have been created for this build.
            // LocalTlogLocation is where the build writes the tracking logs.
            if (!File.Exists(Path.Combine(LocalTlogLocation, "CL.read.1.tlog"))) return true;
            if (!File.Exists(Path.Combine(LocalTlogLocation, "CL.write.1.tlog"))) return true;
            if (!File.Exists(Path.Combine(LocalTlogLocation, "link.read.1.tlog"))) return true;
            if (!File.Exists(Path.Combine(LocalTlogLocation, "link.write.1.tlog"))) return true;
            LogProjectMessage("Build tracking logs found");

            // If tracking log cache exists, ready it for resetting
            var completeMarker = Path.Combine(tlogCacheLocation, "complete");
            if (File.Exists(completeMarker))
            {
                try
                {
                    File.Delete(completeMarker);
                }
                catch (Exception)
                {
                    Log.LogWarning("Cannot delete " + completeMarker + " skipping cache");
                    return true;
                }
            }

            // Copy built tracking logs to tlog cache directory
            var buildTLogFiles = Directory.GetFiles(LocalTlogLocation, "*.1.tlog");
            foreach (var buildTLogFile in buildTLogFiles)
            {
                LogProjectMessage(string.Format("Copy built log {0} to {1}", buildTLogFile, tlogCacheLocation));
                if (!Directory.Exists(tlogCacheLocation))
                    Directory.CreateDirectory(tlogCacheLocation);
                try
                {
                    File.Copy(buildTLogFile, Path.Combine(tlogCacheLocation, Path.GetFileName(buildTLogFile)));
                }
                catch (Exception)
                {
                    Log.LogWarning("Cannot copy to " + tlogCacheLocation + " skipping cache");
                    return true;
                }
            }

            // Parse real inputs and outputs
            IList<string> realInputs, realOutputs;
            if (!ParseRealInputsAndOutputs(tlogCacheLocation, out realInputs, out realOutputs))
                return true;

            var inputFilesHash = GetHashForFiles(realInputs);
            // Must not mix caches across different build configs
            inputFilesHash = GetHashForContent(inputFilesHash + BuildConfig);

            // Cache the actual built binaries
            return CacheBuildArtifacts(realOutputs, inputFilesHash);
        }
    }
}
