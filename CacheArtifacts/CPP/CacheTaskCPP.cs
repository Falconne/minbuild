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

        protected override string GetCacheType()
        {
            return "cpp";
        }

        protected string GetTLogCacheLocation()
        {
            LogProjectMessage("Calculating tlog cache location from inputs");
            var inputFiles = ParseFileList(Inputs).Where(File.Exists).ToList();
            foreach (var inputFile in inputFiles)
            {
                if (!File.Exists(inputFile))
                {
                    LogProjectMessage("Creating non existent file " + inputFile);
                    File.Create(inputFile);
                }
            }
            var cppInput = GetHashForFiles(inputFiles);
            cppInput = GetHashForContent(cppInput + BuildConfig);
            return GetCacheDirForHash(cppInput);
        }

        // Link TLog is different for .exe builds and .lib builds
        protected void EvaluateLinkTLogFilename(string tlogCacheLocation)
        {
            LinkTLogFilename = "link.write.1.tlog";
            var linkTLog = Path.Combine(tlogCacheLocation, LinkTLogFilename);
            if (!File.Exists(linkTLog))
            {
                LinkTLogFilename = "Lib-link.write.1.tlog";
            }

            LogProjectMessage("Link tlog evaluated as " + LinkTLogFilename);
        }

        protected bool ParseRealInputsAndOutputs(string tlogCacheLocation, 
            out IList<string> realInputs, out IList<string> realOutputs)
        {
            realInputs = null;
            realOutputs = null;

            if (string.IsNullOrWhiteSpace(tlogCacheLocation) || !Directory.Exists(tlogCacheLocation))
                return false;

            LogProjectMessage("Parsing real inputs and outputs from tlogs.", MessageImportance.Low);
            var allInputs = new List<string>();
            var inputTLogFiles = Directory.GetFiles(tlogCacheLocation, "*.read.1.tlog");
            foreach (var inputTLogFile in inputTLogFiles)
            {
                var inputs = ReadTlogFile(tlogCacheLocation, inputTLogFile);
                if (inputs == null)
                    continue;
                allInputs.AddRange(inputs);
            }

            LogProjectMessage("All inputs before filter:", MessageImportance.Low);
            allInputs.ForEach(x => LogProjectMessage("\t" + x, MessageImportance.Low));

            // Real inputs are our source files and linker inputs minus objects from our own
            // compile (i.e.  other libs).
            var intermediateOutputFiles = Directory.GetFiles(tlogCacheLocation, "*.write.1.tlog").Where(
                x => !x.Contains("link.write.1.tlog"));
            intermediateOutputFiles = intermediateOutputFiles.Where(x => !x.ToLower().EndsWith(".pdb"));
            allInputs.RemoveAll(x => intermediateOutputFiles.Contains(x));
            allInputs.RemoveAll(x => !File.Exists(x));

            LogProjectMessage("All inputs after filter:", MessageImportance.Low);
            allInputs.ForEach(x => LogProjectMessage("\t" + x, MessageImportance.Low));
            if (allInputs.Count == 0) return false;
            realInputs = allInputs;

            realOutputs = ReadTlogFile(tlogCacheLocation, LinkTLogFilename).ToList();
            if (realOutputs.Count == 0) return false;

            LogProjectMessage("Real Outputs:", MessageImportance.Low);
            foreach (var realOutput in realOutputs)
            {
                LogProjectMessage("\t" + realOutput, MessageImportance.Low);
            }

            return true;
        }

        protected string LinkTLogFilename;

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
            
            var ignoreComments = name.ToLower().Contains(".write.1.tlog");
            var filteredLines = ignoreComments ?
                lines.Where(x => !x.StartsWith("^")) :
                lines.Select(x => x.Replace("^", ""));

            filteredLines = filteredLines.Select(x => x.ToUpper());
            filteredLines = filteredLines.Where(x => 
                !x.EndsWith(".ASSEMBLYATTRIBUTES.CPP") &&
                !x.Contains("|") &&
                !x.EndsWith(".TLOG") &&
                !x.EndsWith(".OBJ"));

            LogProjectMessage("Content from " + name, MessageImportance.Low);
            foreach (var filteredLine in filteredLines)
            {
                LogProjectMessage("\t" + filteredLine, MessageImportance.Low);
            }

            return filteredLines;
        }
    }
}
