using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Framework.XamlTypes;

namespace MinBuild
{
    public class CheckCompileCacheCSharp : CacheTaskCSharp
    {
        [Required]
        public string Inputs { private get; set; }

        [Output]
        public string InputHash { get; private set; }

        public override bool Execute()
        {
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

            InputHash = RestoreCachedArtifactsIfPossible(inputFiles, outputFiles);

            return true;
        }
    }
}
