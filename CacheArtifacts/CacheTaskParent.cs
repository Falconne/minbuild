﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MinBuild
{
    public abstract class CacheTaskParent : Task
    {
        [Required]
        public string ProjectName { get; set; }

        [Required]
        public string BranchVersion { get; set; }

        [Required]
        public string CacheRoot { get; set; }

        [Required]
        public bool ShowContentHashes { private get; set; }

        [Required]
        public bool ShowRecompileReason { private get; set; }

        [Required]
        public bool AlwaysRestoreCache { private get; set; }

        [Required]
        public string BuildConfig { protected get; set; }
        

        protected IList<string> ParseFileList(string raw)
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLower();
            var pfDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLower();
            pfDir = pfDir.Replace(" (x86)", "");

            var inputFiles =
                raw.Split(';').Where(x =>
                    !string.IsNullOrWhiteSpace(x)).Select(x => x.ToLower().Trim());

            var uniqueFiles = inputFiles.Where(x =>
                !x.StartsWith(pfDir) &&
                !x.EndsWith("assemblyattributes.cs") &&
                !x.EndsWith("assemblyinfo.cs") &&
                !x.StartsWith(windowsDir) &&
                !x.EndsWith(".rc")).ToList();

            // Generated project names are random, so don't include them in the sorting. They would move
            // around from build to build and change the content hash.
            var tmpProj = uniqueFiles.FirstOrDefault(x => x.EndsWith(".tmp_proj"));
            if (!string.IsNullOrWhiteSpace(tmpProj))
            {
                LogProjectMessage("Moving temporary project to end of list: " + tmpProj);
                uniqueFiles = uniqueFiles.Where(x => x != tmpProj).ToList();
            }

            uniqueFiles = uniqueFiles.OrderBy(z => z).Distinct().ToList();

            if (!string.IsNullOrWhiteSpace(tmpProj))
            {
                uniqueFiles.Add(tmpProj);
            }

            return uniqueFiles;
        }

        protected void CheckForMissingInputs(IList<string> inputFiles)
        {
            foreach (var inputFile in inputFiles.Where(x => !File.Exists(x)))
            {
                Log.LogError(ProjectName + ": Input file is missing " + inputFile);
                File.Create(inputFile);
            }
        }

        protected bool ShouldSkipCache(IEnumerable<string> outputFiles)
        {
            var hashset = new HashSet<string>();
            if (outputFiles.Any(file => !hashset.Add(Path.GetFileName(file))))
            {
                Log.LogWarning("Cache cannot be used with duplicate output files:");
                var duplicates = outputFiles.Where(file => !hashset.Add(Path.GetFileName(file))).ToList();
                duplicates.ForEach(x => Log.LogWarning("\t" + x));
                return true;
            }

            return false;
        }

        protected void LogProjectMessage(string message, 
            MessageImportance importance = MessageImportance.High)
        {
            Log.LogMessage(importance, string.Format("{0}: {1}", ProjectName, message));
        }

        protected abstract string GetCacheType();

        // We use a separate cache for each major version so we don't have to worry about version info.
        // That is, a cached file from any branch in this major version is ok to use.
        // TODO Rename Branch to Release
        protected string BranchCacheLocation { get { return Path.Combine(CacheLocation, BranchVersion); } }

        protected string PrecomputedCacheLocation { get { return Path.Combine(CacheLocation, "Precomputed"); } }

        // Copy the built output files to the appropriate cache directory based on the input hash
        protected bool CacheBuildArtifacts(IList<string> outputFiles, string cacheHash)
        {
            if (ShouldSkipCache(outputFiles))
                return true;

            var cacheOutput = Path.Combine(BranchCacheLocation, cacheHash);
            if (string.IsNullOrWhiteSpace(cacheOutput)) return true;
            LogProjectMessage("Caching artifacts to " + cacheOutput);
            if (!Directory.Exists(cacheOutput))
                Directory.CreateDirectory(cacheOutput);

            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + outputFile);
                var dst = Path.Combine(cacheOutput, Path.GetFileName(outputFile));
                if (File.Exists(dst))
                {
                    Log.LogWarning(ProjectName + ": Overwriting cached file " + dst);
                    File.Delete(dst);
                }

                File.Copy(outputFile, dst);
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            File.Create(completeMarker).Close();

            return true;
        }

        protected string RestoreCachedArtifactsIfPossible(IList<string> inputFiles, IList<string> outputFiles,
            out bool restoreSuccessful)
        {
            restoreSuccessful = false;
            var recompileReasonPriority = (ShowRecompileReason)
                ? MessageImportance.High
                : MessageImportance.Normal;

            if (outputFiles == null || outputFiles.Count == 0)
                throw new Exception("Cache restore called with no output files");
            LogProjectMessage("Recompile requested: " + BuildConfig);
            LogProjectMessage("\tRecompile reason:", MessageImportance.Normal);
            var missingOutputFiles = outputFiles.Where(x => !File.Exists(x)).ToList();
            if (missingOutputFiles.Any())
            {
                LogProjectMessage("\t\tMissing outputs:", recompileReasonPriority);
                missingOutputFiles.ForEach(x =>
                    LogProjectMessage("\t\t\t" + Path.GetFullPath(x), recompileReasonPriority));
            }
            else
            {
                LogProjectMessage("\t\tOutput files are:", MessageImportance.Normal);
                foreach (var outputFile in outputFiles)
                {
                    LogProjectMessage("\t\t\t" + Path.GetFullPath(outputFile), MessageImportance.Normal);
                }

                LogProjectMessage("\t\tOne or more inputs may have changed:", recompileReasonPriority);
                var outputFileInfos = outputFiles.Select(x => new FileInfo(x)).ToList();
                var oldestOutputFile = outputFileInfos.OrderBy(x => x.LastWriteTime).First();
                var oldestOutputTime = oldestOutputFile.LastWriteTime;
                LogProjectMessage("Oldest output file is " + oldestOutputFile);

                var hasInputChanged = false;
                foreach (var inputFile in inputFiles)
                {
                    var fi = new FileInfo(inputFile);
                    if (fi.LastWriteTime < oldestOutputTime) continue;
                    LogProjectMessage("\t\t\t" + Path.GetFullPath(inputFile), recompileReasonPriority);
                    hasInputChanged = true;
                    LogProjectMessage("\t\t\tNot checking for any more changed inputs", recompileReasonPriority);
                    break;
                }

                if (!hasInputChanged)
                {
                    if (!AlwaysRestoreCache)
                    {
                        LogProjectMessage("Outputs are upto date, not checking cache.");
                        restoreSuccessful = true;
                        return "SKIP";
                    }
                    LogProjectMessage("Outputs are upto date, but restoring anyway due to AlwaysRestoreCache switch.");
                }
            }

            var inputHash = GetHashForFiles(inputFiles);
            inputHash = GetHashForContent(inputHash + BuildConfig);
            var cacheOutput = GetExistingCacheDirForHash(inputHash);
            if (string.IsNullOrWhiteSpace(cacheOutput)) return inputHash;

            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + Path.GetFullPath(outputFile), MessageImportance.Normal);
                var filename = Path.GetFileName(outputFile);
                // ReSharper disable once AssignNullToNotNullAttribute
                var src = Path.Combine(cacheOutput, filename);
                if (!File.Exists(src))
                {
                    LogProjectMessage(string.Format("\t\tCache file {0} missing, recompiling...", filename));
                    return inputHash;
                }

                while (true)
                {
                    try
                    {
                        if (File.Exists(outputFile))
                            File.Delete(outputFile);

                        var outputDir = Path.GetDirectoryName(outputFile);
                        if (!Directory.Exists(outputDir))
                            Directory.CreateDirectory(outputDir);

                        LogProjectMessage("Restoring cached file to " + outputFile);
                        File.Copy(src, outputFile);
                        File.SetLastWriteTimeUtc(outputFile, DateTime.UtcNow);
                        break;
                    }
                    catch (IOException e)
                    {
                        Log.LogWarning(string.Format("Can't modify {0}, retrying...", e.Message));
                        Thread.Sleep(100);
                    }
                }
            }

            restoreSuccessful = true;
            return inputHash;
        }

        // Content hash is the hash of each input file's content, concatenanted then rehashed
        protected string GetHashForFiles(IList<string> files)
        {
            var sb = new StringBuilder(48 * (files.Count() + 5));
            var priority = (ShowContentHashes) ? MessageImportance.High : MessageImportance.Low;
            LogProjectMessage(string.Format("Generating hashes for {0} files", files.Count), priority);
            foreach (var file in files)
            {
                LogProjectMessage("\t\t\tInput: " + file, priority);
                var fileHash = GetHashForFile(file);
                sb.Append(fileHash);
            }

            var hashString = GetHashForContent(sb.ToString());
            LogProjectMessage("Generated Hash for files: " + hashString, priority);

            return hashString;
        }

        protected string GetHashForContent(byte[] bytes)
        {
            var cryptoProvider = new MD5CryptoServiceProvider();
            var hashString = BitConverter.ToString(cryptoProvider.ComputeHash(bytes));
            var priority = (ShowContentHashes) ? MessageImportance.High : MessageImportance.Low;
            LogProjectMessage("Generated Hash for content: " + hashString, priority);

            return hashString;
        }

        protected string GetHashForContent(string raw)
        {
            return GetHashForContent(Encoding.Default.GetBytes(raw));
        }

        protected string GetHashForFile(string file, bool allowPrecompute = true)
        {
            if (allowPrecompute)
            {
                // TODO detect architecture, be smarter (remember, msbuild tasks run 32bit)
                if (file.ToLower().Contains(@"\program files"))
                {
                    return GetPrecomputedHashFor(file);
                }
            }

            var bytes = GetBytesWithRetry(file);
            return GetHashForContent(bytes);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke")]
        protected static byte[] GetBytesWithRetry(string path)
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
        protected string GetPrecomputedHashFor(string filepath)
        {
            LogProjectMessage("Checking for precomputed hash for " + filepath, MessageImportance.Low);
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
                Log.LogWarning("Reason " + e.Message);
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

        protected string GetCacheDirForHash(string hash)
        {
            return Path.Combine(BranchCacheLocation, hash);
        }

        protected string GetExistingCacheDirForHash(string hash)
        {
            var cacheOutput = GetCacheDirForHash(hash);
            if (!Directory.Exists(cacheOutput))
            {
                LogProjectMessage("Artifacts not cached, recompiling...");
                return null;
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            if (!File.Exists(completeMarker))
            {
                LogProjectMessage("Cache dir incomplete, recompiling...");
                return null;
            }

            LogProjectMessage("Retrieving cached artifacts from " + cacheOutput);
            // Touch to reset deletion timer
            Directory.SetLastWriteTimeUtc(cacheOutput, DateTime.UtcNow);

            return cacheOutput;
        }

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private const long ERROR_LOCK_VIOLATION = 0x21;

        private string CacheLocation
        {
            get { return Path.Combine(CacheRoot, GetCacheType()); }
        }

    }
}