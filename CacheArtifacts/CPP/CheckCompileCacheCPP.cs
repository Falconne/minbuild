using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild
{
    public class CheckCompileCacheCPP : CacheTaskCPP
    {
        [Required]
        public string Inputs { private get; set; }

        [Required]
        public string BuildConfig { private get; set; }


        public override bool Execute()
        {
            LogProjectMessage("Recompile requested, checking for cached artifacts");
            LogProjectMessage("Build configuration: " + BuildConfig);
            var inputFiles = ParseFileList(Inputs).ToList();
            var cppInput = GetHashForFiles(inputFiles);
            cppInput = GetHashForContent(cppInput + BuildConfig);
            var tlogCacheLocation = GetCacheDirForHash(cppInput);
            if (string.IsNullOrWhiteSpace(tlogCacheLocation)) return true;

            var compileInputs = ReadTlogFile(tlogCacheLocation, "CL.read.1.tlog", false);
            var compileOutputs = ReadTlogFile(tlogCacheLocation, "CL.write.1.tlog", true);
            var linkInputs = ReadTlogFile(tlogCacheLocation, "link.read.1.tlog", true);
            var linkOutputs = ReadTlogFile(tlogCacheLocation, "link.read.1.tlog", true);

            // Real inputs are our source files and linker inputs minus objects from our own
            // compile (i.e.  other libs).
            var externalLinkInputs = linkInputs.Where(x => !compileOutputs.Contains(x));

            var realInputs = compileInputs.Concat(externalLinkInputs).ToList();
            LogProjectMessage("Inputs:");
            realInputs.ForEach(x => LogProjectMessage("\t" + x));

            return true;
        }

        private IEnumerable<string> ReadTlogFile(string tlogCacheLocation, string name, bool ignoreComments)
        {
            var tlog = Path.Combine(tlogCacheLocation, name);
            if (!File.Exists(tlog))
            {
                LogProjectMessage(tlog + " not found, skipping cache");
                return null;
            }

            var lines = File.ReadAllLines(tlog);
            IEnumerable<string> filteredLines;
            filteredLines = ignoreComments ? 
                lines.Where(x => !x.StartsWith("^")) : 
                lines.Select(x => x.Replace("^", ""));

            return filteredLines;
        } 
    }
}
