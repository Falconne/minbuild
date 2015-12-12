using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CacheArtifacts : CacheTaskParent
    {
        [Required]
        public string InputHash { private get; set; }

        public override bool Execute()
        {
            var outputFiles = ParseFileList(Outputs);
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var cacheOutput = Path.Combine(CacheLocation, InputHash);
            LogProjectMessage("Caching artifacts to " + cacheOutput);
            if (Directory.Exists(cacheOutput))
            {
                LogProjectMessage("Cache dir is being created by another thread");
                return true;
            }

            Directory.CreateDirectory(cacheOutput);
            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + outputFile);
                var dst = Path.Combine(cacheOutput, Path.GetFileName(outputFile));
                if (File.Exists(dst))
                {
                    LogProjectMessage("Cache files are being created by another thread");
                    return true;
                }

                File.Copy(outputFile, dst);
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            File.Create(completeMarker);
            
            return true;
        }
    }
}
