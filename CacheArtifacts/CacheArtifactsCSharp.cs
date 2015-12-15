using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CacheArtifactsCSharp : CacheTaskParent
    {
        [Required]
        public string InputHash { private get; set; }

        public override bool Execute()
        {
            var outputFiles = ParseFileList(Outputs).ToList();
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var cacheOutput = Path.Combine(BranchCacheLocation, InputHash);
            LogProjectMessage("Caching artifacts to " + cacheOutput, MessageImportance.Normal);
            if (Directory.Exists(cacheOutput))
            {
                Log.LogWarning(ProjectName + ": Cache dir is being created by another thread");
                return true;
            }

            Directory.CreateDirectory(cacheOutput);
            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + outputFile, MessageImportance.Normal);
                var dst = Path.Combine(cacheOutput, Path.GetFileName(outputFile));
                if (File.Exists(dst))
                {
                    Log.LogWarning(ProjectName + ": Cache files are being created by another thread");
                    return true;
                }

                File.Copy(outputFile, dst);
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            File.Create(completeMarker).Close();
            
            return true;
        }
    }
}
