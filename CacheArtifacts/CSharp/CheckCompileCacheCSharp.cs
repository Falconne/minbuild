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

        [Required]
        public string BuildConfig { private get; set; }

        [Required]
        public bool ShowRecompileReason { private get; set; }

        [Output]
        public string InputHash { get; private set; }

        public override bool Execute()
        {
            var recompileReasonPriority = (ShowRecompileReason) ? 
                MessageImportance.High : MessageImportance.Normal;
            LogProjectMessage("Recompile requested, checking for cached artifacts", recompileReasonPriority);
            LogProjectMessage("Build configuration: " + BuildConfig);
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

            LogProjectMessage("\tRecompile reason:", recompileReasonPriority);
            var missingOutputFiles = outputFiles.Where(x => !File.Exists(x)).ToList();
            if (missingOutputFiles.Any())
            {
                LogProjectMessage("\t\tMissing outputs:", recompileReasonPriority);
                missingOutputFiles.ForEach(x => 
                    LogProjectMessage("\t\t\t" + Path.GetFullPath(x), recompileReasonPriority));
            }
            else
            {
                var outputFilesAccessTimes = outputFiles.Select(x => new FileInfo(x).LastWriteTime);
                var oldestOutputTime = outputFilesAccessTimes.OrderBy(x => x).First();
                LogProjectMessage("\t\tOutputs are:", recompileReasonPriority);
                outputFiles.ForEach(x => LogProjectMessage(
                    "\t\t\t" + Path.GetFullPath(x), recompileReasonPriority));
                LogProjectMessage("\t\tOne or more inputs have changed:", recompileReasonPriority);
                foreach (var inputFile in inputFiles)
                {
                    var fi = new FileInfo(inputFile);
                    if (fi.LastWriteTime > oldestOutputTime)
                    {
                        LogProjectMessage("\t\t\t" + Path.GetFullPath(inputFile), recompileReasonPriority);
                    }
                }
            }

            InputHash = GetHashForFiles(inputFiles);
            InputHash = GetHashForContent(InputHash + BuildConfig);
            var cacheOutput = GetExistingCacheDirForHash(InputHash);
            if (string.IsNullOrWhiteSpace(cacheOutput)) return true;

            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + Path.GetFullPath(outputFile), MessageImportance.Normal);
                var filename = Path.GetFileName(outputFile);
                var src = Path.Combine(cacheOutput, filename);
                if (!File.Exists(src))
                {
                    LogProjectMessage("\t\tCache file missing, recompiling...");
                    return true;
                }
                if (File.Exists(outputFile))
                    File.Delete(outputFile);
                File.Copy(src, outputFile);
                File.SetLastWriteTimeUtc(outputFile, DateTime.UtcNow);
            }

            return true;
        }
    }
}
