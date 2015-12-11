using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CacheArtifacts
{
    public class CacheArtifacts : Task
    {
        public override bool Execute()
        {
            var inputFiles = Inputs.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim());
            var outputFiles = Outputs.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim());
            Log.LogMessage(MessageImportance.High, "Hello from CacheArtifacts. Inputs: ");
            foreach (var inputFile in inputFiles)
            {
                Log.LogMessage(MessageImportance.High, "\t" + inputFile);
            }
            Log.LogMessage(MessageImportance.High, "Hello from CacheArtifacts. Ouptuts: ");
            foreach (var outputFile in outputFiles)
            {
                Log.LogMessage(MessageImportance.High, "\t" + outputFile);
            }

            return true;
        }

        [Required]
        public string Inputs { get; set; }


        [Required]
        public string Outputs { get; set; }
    }
}
