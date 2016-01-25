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
            var outputFiles = ParseFileList(Outputs);
            if (ShouldSkipCache(outputFiles))
            {
                return true;
            }

            var inputFiles = ParseFileList(Inputs);
            CheckForMissingInputs(inputFiles);

            bool dummy;
            InputHash = RestoreCachedArtifactsIfPossible(inputFiles, outputFiles, out dummy);
            LogProjectMessage("InputHash returned: " + InputHash, MessageImportance.Normal);

            return true;
        }
    }
}
