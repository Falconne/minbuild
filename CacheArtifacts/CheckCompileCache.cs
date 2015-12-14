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

namespace MinBuild
{
    public class CheckCompileCache : CacheTaskParent
    {
        [Required]
        public string Inputs { private get; set; }

        [Output]
        public string InputHash { get; private set; }

        public override bool Execute()
        {
            // FIXME restrict to branch version
            LogProjectMessage("Recompile requested, checking for cached artifacts", MessageImportance.Normal);
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

            LogProjectMessage("\tRecompile reason:", MessageImportance.Normal);
            var missingOutputFiles = outputFiles.Where(x => !File.Exists(x)).ToList();
            if (missingOutputFiles.Any())
            {
                LogProjectMessage("\t\tMissing outputs:", MessageImportance.Normal);
                missingOutputFiles.ForEach(x => 
                    LogProjectMessage("\t\t\t" + Path.GetFullPath(x), MessageImportance.Normal));
            }
            else
            {
                var outputFilesAccessTimes = outputFiles.Select(x => new FileInfo(x).LastWriteTime);
                var oldestOutputTime = outputFilesAccessTimes.OrderBy(x => x).First();
                LogProjectMessage("\t\tOutputs are:", MessageImportance.Normal);
                outputFiles.ForEach(x => LogProjectMessage(
                    "\t\t\t" + Path.GetFullPath(x), MessageImportance.Normal));
                LogProjectMessage("\t\tOne or more inputs have changed:", MessageImportance.Normal);
                foreach (var inputFile in inputFiles)
                {
                    var fi = new FileInfo(inputFile);
                    if (fi.LastWriteTime > oldestOutputTime)
                    {
                        LogProjectMessage("\t\t\t" + Path.GetFullPath(inputFile), MessageImportance.Normal);
                    }
                }
            }

            InputHash = GetHashForFiles(inputFiles);
            var cacheOutput = Path.Combine(BranchCacheLocation, InputHash);
            if (!Directory.Exists(cacheOutput))
            {
                LogProjectMessage("Artifacts not cached, recompiling...");
                return true;
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            if (!File.Exists(completeMarker))
            {
                LogProjectMessage("Cache dir incomplete, recompiling...");
                return true;
            }

            LogProjectMessage("Retrieving cached artifacts from " + cacheOutput);
            // Touch to reset deletion timer
            File.SetLastWriteTimeUtc(completeMarker, DateTime.UtcNow);
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

        // Content hash is the hash of each input file's content, concatenanted then rehashed
        private string GetHashForFiles(IList<string> files)
        {
            // TODO detect architecture
            var sb = new StringBuilder(48 * (files.Count() + 5));
            foreach (var file in files)
            {
                LogProjectMessage("\t\t\tInput: " + file, MessageImportance.Low);
                var fileHash = GetHashForFile(file);
                LogProjectMessage("\t\t\tHash: " + fileHash, MessageImportance.Low);
                sb.Append(fileHash);
            }

            var hashString = GetHashForContent(sb.ToString());
            return hashString;
        }

        private string GetHashForContent(byte[] bytes)
        {
            var cryptoProvider = new MD5CryptoServiceProvider();
            var hashString = BitConverter.ToString(cryptoProvider.ComputeHash(bytes));

            return hashString;
        }

        private string GetHashForContent(string raw)
        {
            return GetHashForContent(Encoding.Default.GetBytes(raw));
        }

        private string GetHashForFile(string file, bool allowPrecompute = true)
        {
            if (allowPrecompute)
            {
                // TODO be smarter (remember, msbuild tasks run 32bit)
                if (file.Contains(@"\Program Files"))
                {
                    return GetPrecomputedHashFor(file);
                }
            }

            var bytes = GetBytesWithRetry(file);
            return GetHashForContent(bytes);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke")]
        private static byte[] GetBytesWithRetry(string path)
        {
            var autoResetEvent = new AutoResetEvent(false);

            while (true)
            {
                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    // Multithreaded builds will take exclusive locks on shared files
                    var win32Error = Marshal.GetLastWin32Error();

                    if (win32Error != ERROR_SHARING_VIOLATION && win32Error != ERROR_LOCK_VIOLATION) continue;

                    using (var fileSystemWatcher = new FileSystemWatcher(Path.GetDirectoryName(path),
                            "*" + Path.GetExtension(path))
                    {
                        EnableRaisingEvents = true
                    })
                    {
                        fileSystemWatcher.Changed +=
                            (o, e) =>
                            {
                                if (Path.GetFullPath(e.FullPath) == Path.GetFullPath(path))
                                {
                                    autoResetEvent.Set();
                                }
                            };

                        autoResetEvent.WaitOne();
                    }
                }
            }
        }

        // Cache and retrieve procomputed content hash for SDK input files
        private string GetPrecomputedHashFor(string filepath)
        {
            //LogProjectMessage("Checking for precomputed hash for " + filepath);
            var fi = new FileInfo(filepath);
            var dirAsSubpath = fi.FullName.Replace(":", "");
            var cachePath = Path.Combine(PrecomputedCacheLocation, dirAsSubpath);

            if (Directory.Exists(cachePath))
            {
                var hashFile = Directory.EnumerateFiles(cachePath, "*.hash").FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(hashFile))
                {
                    var hfi = new FileInfo(hashFile);
                    if (hfi.LastWriteTime != fi.LastWriteTime)
                    {
                        LogProjectMessage("Precomputed hash out of date for " + filepath);
                    }
                    else
                    {
                        return Path.GetFileNameWithoutExtension(hashFile);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(cachePath);
            }

            var hash = GetHashForFile(filepath, false);
            var newHashFile = Path.Combine(cachePath, hash + ".hash");

            // As build is multithreaded, other threads could be writing this file out.
            // Ignore any write errors as it doesn't mater who updates the cache file.
            try
            {
                File.Create(newHashFile).Close();
            }
            catch (IOException e)
            {
                Log.LogWarning("Cannot create precomputed hash " + newHashFile);
                return hash;
            }
            try
            {
                // Used to ensure we recompute if the source file is modified
                File.SetLastWriteTime(newHashFile, fi.LastWriteTime);
            }
            catch (IOException)
            {
                Log.LogWarning("Cannot set last modified time on " + newHashFile);
            }

            return hash;
        }

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private const long ERROR_LOCK_VIOLATION = 0x21;
    }
}
