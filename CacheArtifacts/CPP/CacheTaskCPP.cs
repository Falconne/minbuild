using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild
{
    public abstract class CacheTaskCPP : CacheTaskParent
    {
        [Required]
        public string Inputs { protected get; set; }

        [Required]
        public string BuildConfig { protected get; set; }

        protected override string GetCacheType()
        {
            return "cpp";
        }

        protected string GetTLogCacheLocation()
        {
            var inputFiles = ParseFileList(Inputs).ToList();
            var cppInput = GetHashForFiles(inputFiles);
            cppInput = GetHashForContent(cppInput + BuildConfig);
            return GetCacheDirForHash(cppInput);
        }

        protected bool ParseRealInputsAndOutputs(string tlogCacheLocation, 
            out IList<string> realInputs, out IList<string> realOutputs)
        {
            realInputs = null;
            realOutputs = null;

            if (string.IsNullOrWhiteSpace(tlogCacheLocation) || !Directory.Exists(tlogCacheLocation))
                return false;

            var allInputs = new List<string>();
            var inputTLogFiles = Directory.GetFiles(tlogCacheLocation, "*.read.1.tlog");
            foreach (var inputTLogFile in inputTLogFiles)
            {
                var inputs = ReadTlogFile(tlogCacheLocation, inputTLogFile);
                if (inputs == null)
                    continue;
                allInputs.AddRange(inputs);
            }

            LogProjectMessage("All inputs before filter:");
            allInputs.ForEach(x => LogProjectMessage("\t" + x));

            // Real inputs are our source files and linker inputs minus objects from our own
            // compile (i.e.  other libs).
            var intermediateOutputTLogFiles = Directory.GetFiles(tlogCacheLocation, "*.write.1.tlog").Where(
                x => !"link.write.1.tlog".Equals(x));
            allInputs.RemoveAll(x => intermediateOutputTLogFiles.Contains(x));

            LogProjectMessage("All inputs after filter:");
            allInputs.ForEach(x => LogProjectMessage("\t" + x));
            if (allInputs.Count == 0) return false;

            realOutputs = ReadTlogFile(tlogCacheLocation, "link.write.1.tlog").ToList();
            if (realOutputs.Count == 0) return false;

            LogProjectMessage("Real Outputs:");
            foreach (var realOutput in realOutputs)
            {
                LogProjectMessage("\t" + realOutput);
            }

            return true;
        }

        private IEnumerable<string> ReadTlogFile(string tlogCacheLocation, string name)
        {
            var tlog = Path.Combine(tlogCacheLocation, name);
            if (!File.Exists(tlog))
            {
                LogProjectMessage(tlog + " not found, recompiling...");
                return null;
            }

            LogProjectMessage("Reading " + tlog);

            var lines = File.ReadAllLines(tlog);
            
            var ignoreComments = "CL.read.1.tlog".Equals(name) || "rc.read.1.tlog".Equals(name);
            var filteredLines = ignoreComments ?
                lines.Where(x => !x.StartsWith("^")) :
                lines.Select(x => x.Replace("^", ""));

            filteredLines = filteredLines.Where(x => !x.EndsWith(".ASSEMBLYATTRIBUTES.CPP"));

            LogProjectMessage("Content from " + name);
            foreach (var filteredLine in filteredLines)
            {
                LogProjectMessage("\t" + filteredLine);
            }

            return filteredLines;
        }
    }
}
