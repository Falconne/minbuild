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
            var inputFiles = Inputs.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim());
            var outputFiles = Outputs.Split(';').Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim());
            /*Log.LogMessage(MessageImportance.High, "****CheckCompileCache. Inputs: ");
            foreach (var inputFile in inputFiles)
            {
                Log.LogMessage(MessageImportance.High, "\t" + inputFile);
            }*/

            Log.LogMessage(MessageImportance.High, "****CheckCompileCache. Ouptuts: ");
            foreach (var outputFile in outputFiles)
            {
                Log.LogMessage(MessageImportance.High, "\t" + outputFile);
                var filename = Path.GetFileName(outputFile);
                var src = Path.Combine(@"c:\temp\minbuild", filename);
                if (!File.Exists(src))
                    return true;
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
