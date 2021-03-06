﻿using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinBuild
{
    public abstract class CacheTaskCPP : CacheTaskParent
    {
        [Required]
        public string Inputs { protected get; set; }

        [Required]
        public string ProjectPath { protected get; set; }

        protected override string GetCacheType()
        {
            return "cpp";
        }

        protected string GetTLogCacheLocation()
        {
            var inputFiles = ParseFileList(Inputs);
            inputFiles.Add(ProjectPath);
            LogProjectMessage("Parsed Inputs:");
            foreach (var inputFile in inputFiles)
            {
                LogProjectMessage(inputFile);
            }

            CheckForMissingInputs(inputFiles);
            var cppInputHash = GetHashForFiles(inputFiles);
            cppInputHash = GetHashForContent(cppInputHash + BuildConfig);
            var loc = GetCacheDirForHash(cppInputHash);
            LogProjectMessage("Tlog cache location: " + loc);

            return loc;
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

        // TLOGs are weird. They need creative parsing.
        protected bool ParseRealInputsAndOutputs(string tlogCacheLocation, out IList<string> realInputs, out IList<string> realOutputs, bool discardFullPaths)
        {
            realInputs = null;
            realOutputs = null;

            if (string.IsNullOrWhiteSpace(tlogCacheLocation) || !Directory.Exists(tlogCacheLocation))
                return false;

            LogProjectMessage("Parsing real inputs and outputs from tlogs.", MessageImportance.Low);
            var allInputs = new List<string> {ProjectPath};
            var inputTLogFiles = Directory.GetFiles(tlogCacheLocation, "*.read.*.tlog");
            foreach (var inputTLogFile in inputTLogFiles)
            {
                var inputs = ReadTlogFile(inputTLogFile, discardFullPaths);
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
                var intermediateOutputs = ReadTlogFile(intermediateOutputFile, discardFullPaths);
                allInputs.RemoveAll(x => intermediateOutputs.Contains(x));

                // Don't discard any intermediate .lib or .pdb files, sometimes they are actual outputs but
                // not mentioned in the final link.write file.
                parsedOutputs.AddRange(intermediateOutputs.Where(x => x.EndsWith(".LIB") || x.EndsWith(".DLL") || x.EndsWith(".EXE") ||
                    (x.EndsWith(".PDB") && intermediateOutputFile.ToLower().Contains("cl.") && !x.Contains("\\VC1"))));
            }
            allInputs.RemoveAll(x => !File.Exists(x));
            if (allInputs.Count == 0) return false;

            parsedOutputs.AddRange(ReadTlogFile(Path.Combine(tlogCacheLocation, LinkTLogFilename), discardFullPaths).ToList());
            if (parsedOutputs.Count == 0) return false;

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

        private IList<string> ReadTlogFile(string tlog, bool discardFullPaths)
        {
            if (!File.Exists(tlog))
            {
                LogProjectMessage(tlog + " not found.");
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

            // FIXME use TEMP env variable
            filteredLines = filteredLines.Select(x => x.Trim().ToUpper()).Where(y => !y.Contains(@"\BUILDAGENT\TEMP"));
            if (discardFullPaths)
            {
                if (filteredLines.Any(x => x.StartsWith(@"C:\BUILDAGENT")))
                {
                    throw new Exception("Skip tlog with full path");
                }
            }
            filteredLines = filteredLines.Where(x =>
                !x.EndsWith(".ASSEMBLYATTRIBUTES.CPP") &&
                !x.Contains("|") &&
                !x.EndsWith(".TLOG") &&
                !x.EndsWith(".METAGEN") &&
                !x.EndsWith(".RC") &&
                !x.EndsWith(".TLH") &&
                !x.EndsWith(".TLI") &&
                !x.EndsWith(".TRN") &&
                !x.EndsWith(".TMP") &&
                !x.Contains("\\APPLICATIONINSIGHTS\\") &&
                !x.Contains("\\APPDATA\\ROAMING\\MICROSOFT\\") && // FIXME use env vars
                !x.Contains("\\APPDATA\\LOCAL\\MICROSOFT\\") && // FIXME use env vars
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
