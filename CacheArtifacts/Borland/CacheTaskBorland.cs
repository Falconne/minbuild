﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    public abstract class CacheTaskBorland : CacheTaskParent
    {
        [Required]
        public string Makefile { private get; set; }

        [Required]
        public string WorkDir { protected get; set; }

        protected override string GetCacheType()
        {
            return "borland";
        }

        protected IList<string> ParseInputFiles()
        {
            var mfloc = Path.Combine(WorkDir, Makefile);
            LogProjectMessage("Makefile " + mfloc, MessageImportance.Normal);
            if (!File.Exists(mfloc))
            {
                throw new Exception(mfloc + " not found");
            }

            var lines = File.ReadAllLines(mfloc);
            var cppSources = ParseSourceType("SOURCE=", lines).Where(x => !x.ToLower().EndsWith(".rc")).ToList();
            cppSources.AddRange(ParseSourceType("LIBS=", lines));
            cppSources.AddRange(ParseSourceType("RES_DEPENDS=", lines));
            if (!cppSources.Any())
                throw new Exception("No sources found in " + mfloc);

            cppSources.Add(Path.Combine(WorkDir, ProjectName));
            cppSources.ForEach(x => LogProjectMessage(x, MessageImportance.Normal));

            var allSources = new List<string>();
            var includes = ParseMakeVariable("CPP_INCLUDE_PATH = ", lines).Select(y => Path.GetFullPath(Path.Combine(WorkDir, y))).ToList();
            if (includes.Count == 0)
            {
                Log.LogError("Not enough include paths found");
            }
            includes.Add(WorkDir);
            LogProjectMessage("Include paths to check:", MessageImportance.Low);
            includes.ForEach(x => LogProjectMessage(x, MessageImportance.Low));
            foreach (var source in cppSources)
            {
                ParseHeadersIn(source, includes, allSources);
            }

            LogProjectMessage("Found total sources:", MessageImportance.Low);
            allSources.ForEach(x => LogProjectMessage(x, MessageImportance.Low));

            return allSources;
        }

        protected string GetInputHash()
        {

            return GetHashForFiles(ParseInputFiles());
        }

        protected IList<string> GetOutputFiles()
        {
            var mfloc = Path.Combine(WorkDir, Makefile);
            LogProjectMessage("Reading output from " + mfloc, MessageImportance.Low);
            var lines = File.ReadAllLines(mfloc);

            var lflags = ParseMakeVariable("LFLAGS=", lines).ToList();
            LogProjectMessage("Found linker flags:", MessageImportance.Low);
            lflags.ForEach(x => LogProjectMessage(x, MessageImportance.Low));
            var importLibExists = (lflags.Any() && lflags.Contains("-Gi"));

            foreach (var line in lines)
            {
                if (!line.StartsWith("TARGET="))
                    continue;

                var cleanLine = line.Replace("\t", "");
                var parts = cleanLine.Split('=').ToList();
                parts.RemoveAt(0);

                var target = parts[0].ToLower();
                if ((target.EndsWith(".dll") || target.EndsWith(".bpl")) && importLibExists)
                {
                    // .lib output is not properly defined in the Makefile
                    var moduleName = Path.GetFileNameWithoutExtension(parts[0]);
                    var targetPath = Path.GetDirectoryName(parts[0]);

                    if (target.EndsWith(".dll") || lflags.Contains("-Gl"))
                    {
                        var libName = moduleName + ".lib";
                        var libPath = Path.Combine(WorkDir, targetPath, libName);
                        LogProjectMessage("Adding libpath: " + libPath);
                        parts.Add(libPath);
                    }

                    if (target.EndsWith(".bpl"))
                    {
                        var libName = moduleName + ".bpi";
                        var libPath = Path.Combine(WorkDir, targetPath, libName);
                        LogProjectMessage("Adding libpath: " + libPath);
                        parts.Add(libPath);
                    }
                }
                parts[0] = Path.Combine(WorkDir, parts[0]);

                LogProjectMessage("Outputs evaluated as:");
                parts.ForEach(x => LogProjectMessage("\t" + x));

                return parts;
            }

            throw new Exception("Target not found in " + mfloc);
        }

        private IEnumerable<string> ParseMakeVariable(string type, IList<string> lines)
        {
            var sources = new List<string>();
            var sourcesFound = false;
            for (var i = 0; i < lines.Count(); i++)
            {
                var line = lines[i];
                if (!sourcesFound)
                {
                    if (!line.StartsWith(type))
                        continue;

                    sourcesFound = true;
                    line = line.Replace(type, "");
                }

                if (line.Contains("="))
                    break;

                line = line.Replace("\t", "");

                sources.AddRange(Regex.Matches(line, @"[\""].+?[\""]|[^ ;]+").Cast<Match>().Select(m => m.Value).Where(
                    y => !y.Equals("\\") && !string.IsNullOrWhiteSpace(y) && !y.Contains("$"))
                    .Select(q => q.Replace("\"", "")));
            }

            return sources;
        }

        private IEnumerable<string> ParseSourceType(string type, IList<string> lines)
        {
            return ParseMakeVariable(type, lines).Select(x => Path.Combine(WorkDir, x)).Where(File.Exists);
        }

        private void ParseHeadersIn(string source, IList<string> includePaths, IList<string> foundHeaders)
        {
            source = Path.GetFullPath(source).ToLower();
            if (foundHeaders.Contains(source))
                return;

            if (!File.Exists(source))
            {
                LogProjectMessage("WARNING: Source file not found: " + source);
                return;
            }

            foundHeaders.Add(source);
            
            LogProjectMessage("Finding headers in " + source, MessageImportance.Low);
            var lines = File.ReadAllLines(source).Where(x => x.Trim().StartsWith("#include"));
            var regex = new Regex("[<\"](.+?)[>\"]");
            var sourceFileDir = Path.GetDirectoryName(source);
            foreach (var line in lines)
            {
                if (!line.Contains("\"") && !line.Contains("<"))
                {
                    LogProjectMessage("Skipping #include MACRO: " + line);
                    continue;
                }

                LogProjectMessage("Checking line for headers: " + line, MessageImportance.Low);
                var m = regex.Match(line);
                if (!m.Success)
                {
                    throw new Exception("Cannot parse header name in " + line);
                }

                var headerPath = m.Groups[1].ToString().ToLower();
                LogProjectMessage("Searching for header " + headerPath, MessageImportance.Low);
                var tryPath = Path.Combine(sourceFileDir, headerPath);
                if (File.Exists(tryPath))
                {
                    ParseHeadersIn(tryPath, includePaths, foundHeaders);
                    continue;
                }

                var headerFound = false;
                foreach (var includePath in includePaths)
                {
                    tryPath = Path.Combine(includePath, headerPath);
                    if (!File.Exists(tryPath)) continue;
                    ParseHeadersIn(tryPath, includePaths, foundHeaders);
                    headerFound = true;
                    break;
                }

                if (!headerFound && line.Contains("\"") && !line.Contains("IGNORE_IF_MISSING"))
                {
                    // System headers can be ignored but all local headers should be checked.
                    Log.LogWarning("Possible missing non system header: " + headerPath + " in " + source);
                    Log.LogWarning("If this is a system/SDK header then use angle brackets instead of quotes.");
                }
            }
        }
    }
}
