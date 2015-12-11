using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MinBuild
{
    public class CheckCompileCache : CacheTaskParent
    {
        public override bool Execute()
        {
            var outputFiles = ParseFileList(Outputs);
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var inputFiles = ParseFileList(Inputs);
            var contentHash = GetContentHash(inputFiles);
            var cacheOutput = Path.Combine(CacheLocation, contentHash);
            if (!Directory.Exists(cacheOutput))
            {
                Log.LogMessage(MessageImportance.High, "Artifacts not cached, recompiling...");
                return true;
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            if (!File.Exists(completeMarker))
            {
                Log.LogMessage(MessageImportance.High, "Cache dir incomplete, recompiling...");
                return true;
            }

            Log.LogMessage(MessageImportance.High, "**** Retrieving cached artifacts");
            foreach (var outputFile in outputFiles)
            {
                Log.LogMessage(MessageImportance.High, "\t" + outputFile);
                var filename = Path.GetFileName(outputFile);
                var src = Path.Combine(cacheOutput, filename);
                if (!File.Exists(src))
                {
                    Log.LogMessage(MessageImportance.High, "Cache file missing, recompiling...");
                    return true;
                }
                Log.LogMessage(MessageImportance.High, "Copying from " + src);
                if (File.Exists(outputFile))
                    File.Delete(outputFile);
                File.Copy(src, outputFile);
                File.SetLastWriteTimeUtc(outputFile, DateTime.UtcNow);
            }

            return true;
        }
    }
}
