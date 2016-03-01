﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace MinBuild.Borland
{
    public abstract class CacheTaskBorland : CacheTaskParent
    {
        [Required]
        public string Makefile { private get; set; }

        [Required]
        public string WorkDir { private get; set; }

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
            var sources = ParseSourceType("SOURCE=", lines).Where(x => !x.ToLower().EndsWith(".rc")).ToList();
            sources.AddRange(ParseSourceType("LIBS=", lines));
            sources.AddRange(ParseSourceType("RES_DEPENDS=", lines));
            if (!sources.Any())
                throw new Exception("No sources found in " + mfloc);

            LogProjectMessage("Found sources: ", MessageImportance.Normal);
            sources.Add(Path.Combine(WorkDir, ProjectName));
            sources.ForEach(x => LogProjectMessage(x, MessageImportance.Normal));

            return sources;
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

                sources.AddRange(Regex.Matches(line, @"[\""].+?[\""]|[^ ]+").Cast<Match>().Select(m => m.Value).Where(
                    y => !y.Equals("\\") && !string.IsNullOrWhiteSpace(y) && !y.Contains("$"))
                    .Select(q => q.Replace("\"", "")));
            }

            return sources;
        }

        private IEnumerable<string> ParseSourceType(string type, IList<string> lines)
        {
            return ParseMakeVariable(type, lines).Select(x => Path.Combine(WorkDir, x)).Where(File.Exists);
        }

    }
}
