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
            LogProjectMessage("Caching after link, if tracking logs found", MessageImportance.Normal);

            var tlogCacheLocation = GetTLogCacheLocation();
            if (string.IsNullOrWhiteSpace(tlogCacheLocation))
                return true;

            // Check that tracking logs have been created for this build.
            // LocalTlogLocation is where the build writes the tracking logs.
            LogProjectMessage("Current directory is " + Directory.GetCurrentDirectory(), MessageImportance.Normal);
            LogProjectMessage("Looking for built tracking logs in " + LocalTlogLocation, MessageImportance.Normal);
            EvaluateLinkTLogFilename(LocalTlogLocation);
            if (!File.Exists(Path.Combine(LocalTlogLocation, "CL.read.1.tlog"))) return true;
            if (!File.Exists(Path.Combine(LocalTlogLocation, "CL.write.1.tlog"))) return true;
            if (!File.Exists(Path.Combine(LocalTlogLocation, LinkTLogFilename))) return true;
            LogProjectMessage("Build tracking logs found", MessageImportance.Normal);

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
            var buildTLogFiles = Directory.GetFiles(LocalTlogLocation, "*.tlog");
            LogProjectMessage(string.Format("Copying built tlogs from {0} to {1}", 
                LocalTlogLocation, tlogCacheLocation));
            foreach (var buildTLogFile in buildTLogFiles)
            {
                LogProjectMessage(string.Format("Removing absolute paths in {0}:", buildTLogFile), MessageImportance.Low);
                var sourceLines = File.ReadAllLines(buildTLogFile);
                List<string> cleanedLines = null;
                if (!string.IsNullOrWhiteSpace(RootDir))
                {
                    if (!RootDir.EndsWith("\\"))
                        RootDir += "\\";
                    LogProjectMessage("RootDir is " + RootDir, MessageImportance.Low);
                    cleanedLines = sourceLines.Select(x => x.Replace(RootDir.ToUpper(), "")).ToList();
                    cleanedLines.ForEach(x => LogProjectMessage(x, MessageImportance.Low));
                }

                LogProjectMessage(string.Format("Writing cleaned tlog {0}", tlogCacheLocation), 
                    MessageImportance.Low);
                var destination = Path.Combine(tlogCacheLocation, Path.GetFileName(buildTLogFile));
                try
                {
                    if (!Directory.Exists(tlogCacheLocation))
                        Directory.CreateDirectory(tlogCacheLocation);
                    if (File.Exists(destination))
                        File.Delete(destination);

                    File.WriteAllLines(destination, cleanedLines);
                }
                catch (Exception e)
                {
                    Log.LogWarning("Cannot copy to " + tlogCacheLocation + " skipping cache");
                    Log.LogWarning("Reason: " + e.Message);
                    return true;
                }
            }
            File.Create(completeMarker).Close();

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
