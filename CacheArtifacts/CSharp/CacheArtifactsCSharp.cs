﻿using System.IO;

using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CacheArtifactsCSharp : CacheTaskCSharp
    {
        [Required]
        public string InputHash { private get; set; }

        public override bool Execute()
        {
            var inputFiles = ParseFileList(Inputs);
            var outputFiles = ParseFileList(Outputs);
            WriteSourceMapFile(inputFiles, outputFiles, Directory.GetCurrentDirectory());
            CacheBuildArtifacts(inputFiles, outputFiles, InputHash);
            return true;
        }
    }
}
