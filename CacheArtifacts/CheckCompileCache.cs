using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CheckCompileCache : CacheTaskParent
    {
        [Required]
        public string Inputs { private get; set; }

        [Output]
        public string InputHash { get; private set; }

        public override bool Execute()
        {
            // FIXME restrict to branch version
            LogProjectMessage("Checking for cached artifacts");
            var outputFiles = ParseFileList(Outputs).ToList();
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var inputFilesRaw = ParseFileList(Inputs);
            // Ignore AssemblyInfo files as version number differences don't matter
            // TODO allow override of this skip
            var inputFiles = inputFilesRaw.Where(
                x => !x.Contains("AssemblyInfo.cs") && File.Exists(x)).ToList();

            InputHash = GetContentHash(inputFiles);
            var cacheOutput = Path.Combine(CacheLocation, InputHash);
            if (!Directory.Exists(cacheOutput))
            {
                LogProjectMessage("Artifacts not cached, recompiling...");
                return true;
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            if (!File.Exists(completeMarker))
            {
                LogProjectMessage("Cache dir incomplete, recompiling...");
                return true;
            }

            LogProjectMessage("Retrieving cached artifacts from " + cacheOutput);
            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + outputFile, MessageImportance.Normal);
                var filename = Path.GetFileName(outputFile);
                var src = Path.Combine(cacheOutput, filename);
                if (!File.Exists(src))
                {
                    LogProjectMessage("\t\tCache file missing, recompiling...");
                    return true;
                }
                if (File.Exists(outputFile))
                    File.Delete(outputFile);
                // Touch source file to reset its deletion timer
                File.SetLastWriteTimeUtc(src, DateTime.UtcNow);
                File.Copy(src, outputFile);
            }

            return true;
        }
    }
}
