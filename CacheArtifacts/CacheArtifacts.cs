using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CacheArtifacts : CacheTaskParent
    {
        public override bool Execute()
        {
            var outputFiles = ParseFileList(Outputs);
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var inputFiles = ParseFileList(Inputs);
            /*Log.LogMessage(MessageImportance.Low, "****CacheArtifacts. Inputs: ");
            foreach (var inputFile in inputFiles)
            {
                Log.LogMessage(MessageImportance.Low, "\t" + inputFile);
            }*/
            var contentHash = GetContentHash(inputFiles);
            var cacheOutput = Path.Combine(CacheLocation, contentHash);

            Log.LogMessage(MessageImportance.High, ProjectName + ": Caching artifacts to " + cacheOutput);
            if (Directory.Exists(cacheOutput))
            {
                Log.LogMessage(MessageImportance.High, ProjectName + ": Cache dir is being created by another thread");
                return true;
            }

            Directory.CreateDirectory(cacheOutput);
            foreach (var outputFile in outputFiles)
            {
                Log.LogMessage(MessageImportance.High, "\t" + outputFile);
                var dst = Path.Combine(cacheOutput, Path.GetFileName(outputFile));
                if (File.Exists(dst))
                {
                    Log.LogMessage(MessageImportance.High, "Cache files are being created by another thread");
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
