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

        public string RootDir { protected get; set; }

        protected override string GetCacheType()
        {
            return "cpp";
        }

        protected string GetTLogCacheLocation()
        {
            LogProjectMessage("Calculating tlog cache location from inputs");
            var inputFiles = ParseFileList(Inputs);
            CheckForMissingInputs(inputFiles);
            var cppInputHash = GetHashForFiles(inputFiles);
            cppInputHash = GetHashForContent(cppInputHash + BuildConfig);
            return GetCacheDirForHash(cppInputHash);
        }

        // Link TLog is different for .exe builds and .lib builds
        protected void EvaluateLinkTLogFilename(string tlogCacheLocation)
        {
            LinkTLogFilename = "link.write.1.tlog";
            var actualLinkFile = Directory.GetFiles(tlogCacheLocation, "*link*write.*.tlog").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(actualLinkFile))
                LinkTLogFilename = Path.GetFileName(actualLinkFile);

            LogProjectMessage("Link tlog evaluated as " + LinkTLogFilename, MessageImportance.Normal);
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
            var inputTLogFiles = Directory.GetFiles(tlogCacheLocation, "*.read.*.tlog");
            foreach (var inputTLogFile in inputTLogFiles)
            {
                var inputs = ReadTlogFile(inputTLogFile);
                if (inputs.Any(x => x.ToLower().StartsWith(@"c:\buildagent")))
                {
                    LogProjectMessage("Ignoring existing tlog with absolute paths");
                    return false;
                }

                if (inputs == null)
                    continue;
                allInputs.AddRange(inputs);
            }

            allInputs = allInputs.OrderBy(z => z).Distinct().ToList();

            LogProjectMessage("All inputs before filter:", MessageImportance.Low);
            allInputs.ForEach(x => LogProjectMessage("\t" + x, MessageImportance.Low));

            // Real inputs are our source files and linker inputs minus objects from our own
            // compile (i.e. other libs).
            var intermediateOutputFiles = Directory.GetFiles(tlogCacheLocation, "*.write.*.tlog").Where(
                x => !x.Contains("link.write.")).ToList();
            var parsedOutputs = new List<string>();
            foreach (var intermediateOutputFile in intermediateOutputFiles)
            {
                var intermediateOutputs = ReadTlogFile(intermediateOutputFile);
                allInputs.RemoveAll(x => intermediateOutputs.Contains(x));

                // Don't discard any intermediate .lib or .pdb files, sometimes they are actual outputs but
                // not mentioned in the final link.write file.
                parsedOutputs.AddRange(intermediateOutputs.Where(x => x.EndsWith(".LIB") || x.EndsWith(".DLL") || x.EndsWith(".EXE") ||
                    (x.EndsWith(".PDB") && intermediateOutputFile.ToLower().Contains("cl.") && !x.Contains("\\VC1"))));
            }
            allInputs.RemoveAll(x => !File.Exists(x));
            if (allInputs.Count == 0) return false;

            parsedOutputs.AddRange(ReadTlogFile(Path.Combine(tlogCacheLocation, LinkTLogFilename)).ToList());
            if (parsedOutputs.Count == 0) return false;

            if (parsedOutputs.Any(x => x.ToLower().StartsWith(@"c:\buildagent")))
            {
                LogProjectMessage("Ignoring existing tlog with absolute paths");
                return false;
            }

            allInputs.RemoveAll(x => parsedOutputs.Contains(x));
            allInputs = allInputs.OrderBy(x => x).Distinct().ToList();
            LogProjectMessage("All inputs after filter:", MessageImportance.Low);
            allInputs.ForEach(x => LogProjectMessage("\t" + x, MessageImportance.Low));
            realInputs = allInputs;

            LogProjectMessage("Real Outputs:", MessageImportance.Low);
            realOutputs = parsedOutputs.OrderBy(x => x).Distinct().ToList();
            foreach (var realOutput in realOutputs)
            {
                LogProjectMessage("\t" + realOutput, MessageImportance.Low);
            }

            return true;
        }

        protected string LinkTLogFilename;

        private IList<string> ReadTlogFile(string tlog)
        {
            if (!File.Exists(tlog))
            {
                LogProjectMessage(tlog + " not found, recompiling...");
                return null;
            }

            LogProjectMessage("Reading " + tlog, MessageImportance.Low);

            var lines = File.ReadAllLines(tlog);

            var ignoreComments = tlog.ToLower().Contains(".write.");
            var filteredLines = ignoreComments ?
                lines.Where(x => !x.StartsWith("^")) :
                lines.Select(x => x.Replace("^", ""));
            filteredLines = filteredLines.Where(x => !x.StartsWith("#"));

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToUpper();
            var pfDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToUpper();
            pfDir = pfDir.Replace(" (X86)", "");

            filteredLines = filteredLines.Select(x => x.Trim().ToUpper());
            filteredLines = filteredLines.Where(x =>
                !x.EndsWith(".ASSEMBLYATTRIBUTES.CPP") &&
                !x.Contains("|") &&
                !x.EndsWith(".TLOG") &&
                !x.EndsWith(".METAGEN") &&
                !x.EndsWith(".RC") &&
                !x.StartsWith(windowsDir) &&
                !x.StartsWith(pfDir) &&
                !x.EndsWith(".OBJ"));

            filteredLines = filteredLines.OrderBy(x => x).Distinct();
            if (!string.IsNullOrWhiteSpace(RootDir))
                filteredLines = filteredLines.Select(x => Path.Combine(RootDir, x));

            LogProjectMessage("Content from " + tlog, MessageImportance.Low);
            var result = filteredLines.ToList();
            foreach (var filteredLine in result)
            {
                LogProjectMessage("\t" + filteredLine, MessageImportance.Low);
            }

            return result;
        }
    }
}
