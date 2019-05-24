using Microsoft.Build.Framework;
using System;
using System.IO;
using System.Linq;

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
            if (!File.Exists(Path.Combine(LocalTlogLocation, LinkTLogFilename)))
                return true;

            if (Directory.GetFiles(LocalTlogLocation, "CL*.read.*.tlog").Length > 0)
                return true;

            if (Directory.GetFiles(LocalTlogLocation, "CL*.write.*.tlog").Length > 0)
                return true;

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
            LogProjectMessage($"Copying built tlogs from {LocalTlogLocation} to {tlogCacheLocation}");
            foreach (var buildTLogFile in buildTLogFiles)
            {
                LogProjectMessage($"Removing absolute paths in {buildTLogFile}:", MessageImportance.Low);
                var sourceLines = File.ReadAllLines(buildTLogFile).Select(x => x.ToUpper()).ToList();
                if (!string.IsNullOrWhiteSpace(RootDir))
                {
                    if (!RootDir.EndsWith("\\"))
                        RootDir += "\\";
                    LogProjectMessage("RootDir is " + RootDir, MessageImportance.Low);
                    sourceLines = sourceLines.Select(x => x.Replace(RootDir.ToUpper(), "")).ToList();
                }

                LogProjectMessage($"Writing cleaned tlog {buildTLogFile} to {tlogCacheLocation}",
                    MessageImportance.Low);
                var destination = Path.Combine(tlogCacheLocation, Path.GetFileName(buildTLogFile));
                try
                {
                    if (!Directory.Exists(tlogCacheLocation))
                        Directory.CreateDirectory(tlogCacheLocation);
                    if (File.Exists(destination))
                        File.Delete(destination);

                    File.WriteAllLines(destination, sourceLines);
                }
                catch (Exception e)
                {
                    Log.LogWarning("Cannot copy to " + tlogCacheLocation + " skipping cache");
                    Log.LogWarning("Reason: " + e.Message);
                    Log.LogWarning(e.StackTrace);
                    return true;
                }
            }
            File.Create(completeMarker).Close();

            // Parse real inputs and outputs
            if (!ParseRealInputsAndOutputs(tlogCacheLocation, out var realInputs, out var realOutputs, false))
                return true;

            WriteSourceMapFile(realInputs, realOutputs, Directory.GetCurrentDirectory());

            var inputFilesHash = GetHashForFiles(realInputs);
            // Must not mix caches across different build configs
            inputFilesHash = GetHashForContent(inputFilesHash + BuildConfig);

            // Cache the actual built binaries
            CacheBuildArtifacts(realInputs, realOutputs, inputFilesHash);
            return true;
        }
    }
}
