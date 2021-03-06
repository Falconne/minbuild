﻿using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MinBuild
{
    public abstract class CacheTaskParent : Task
    {
        [Required]
        public string ProjectName { get; set; }

        [Required]
        public string CacheRoot { get; set; }

        public string NuGetPackageRoot { get; set; }

        [Required]
        public bool ShowContentHashes { private get; set; }

        [Required]
        public bool ShowRecompileReason { private get; set; }

        [Required]
        public bool AlwaysRestoreCache { private get; set; }

        [Required]
        public string BuildConfig { protected get; set; }

        public string RootDir { protected get; set; }

        public string SkipCacheForProjects { private get; set; }

        private IList<string> _versionKeywordsToIgnore;

        private const string NugetFallbackDirectory = "nugetfallbackfolder";

        protected IList<string> ParseFileList(string rawFileList)
        {
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLower();
            var pfDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLower();
            pfDir = pfDir.Replace(" (x86)", "");

            var files = rawFileList
                .Split(';')
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.ToLower().Trim())
                .Where(x =>
                    (!x.StartsWith(pfDir) || x.Contains(NugetFallbackDirectory)) &&
                    !x.EndsWith("assemblyattributes.cs") &&
                    !x.EndsWith(".corecompileinputs.cache") &&
                    !x.EndsWith(".nuget.g.props") &&
                    !x.EndsWith(".nuget.g.targets") &&
                    !x.StartsWith(windowsDir))
                .Distinct();

            var result = GetFileListWithoutGeneratedProjects(files);
            result.Sort((p1, p2) =>
                GetStablePathForSorting(p1).CompareTo(GetStablePathForSorting(p2)));

            return result;
        }

        private string GetStablePathForSorting(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(NuGetPackageRoot))
                return inputPath;

            if (inputPath.Contains(NuGetPackageRoot.ToLower()))
            {
                return inputPath.Substring(NuGetPackageRoot.Length + 1);
            }

            var nfbIndex = inputPath.IndexOf(NugetFallbackDirectory, StringComparison.Ordinal);
            if (nfbIndex != -1)
            {
                return inputPath.Substring(nfbIndex + NugetFallbackDirectory.Length + 1);
            }
            return inputPath;
        }

        // xaml compile creates generated csproj projects that have non-deterministic
        // content that makes content hashing less useful. Just use the original csproj
        // instead, as that is good enough to know if makefile content has changed.
        private List<string> GetFileListWithoutGeneratedProjects(IEnumerable<string> files)
        {
            var result = files.ToList();
            var tmpProj = result.FirstOrDefault(x => x.EndsWith(".tmp_proj"));
            if (string.IsNullOrWhiteSpace(tmpProj))
                return result;

            LogProjectMessage($"Replacing xaml generated project {tmpProj} with original");

            var projectDirectory = Path.GetDirectoryName(tmpProj);
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return result;

            var csprojFilesInProjectDirectory = Directory.GetFiles(projectDirectory, "*.csproj");
            if (csprojFilesInProjectDirectory.Length == 0)
            {
                LogProjectMessage($"WARNING: Original csproj for {tmpProj}, not found in {csprojFilesInProjectDirectory}");
                return result;
            }

            if (csprojFilesInProjectDirectory.Length > 1)
            {
                LogProjectMessage($"WARNING: Multiple csproj files found in {csprojFilesInProjectDirectory}, so original for {tmpProj} cannot be determined");
                return result;
            }

            var originalProject = csprojFilesInProjectDirectory.First();

            result = result.Where(x => x != tmpProj).ToList();
            LogProjectMessage($"Original project is {originalProject}");
            result.Add(originalProject);

            return result;
        }

        protected void CheckForMissingInputs(IList<string> inputFiles)
        {
            foreach (var inputFile in inputFiles.Where(x => !File.Exists(x)))
            {
                Log.LogError(ProjectName + ": Input file is missing " + inputFile);
                File.Create(inputFile);
            }
        }

        protected bool ShouldSkipCache(IList<string> outputFiles)
        {
            if (outputFiles != null)
            {
                if (IsAnyOutputDuplicated(outputFiles))
                    return true;

                // TODO move to custom assembly
                // TouchPoint branding is used for version display on splash screens
                if (outputFiles.Any(file => file.ToLower().Contains("touchpoint.branding")))
                    return true;
            }

            if (string.IsNullOrWhiteSpace(SkipCacheForProjects))
                return false;

            if (!SkipCacheForProjects.Split('#').Any(s => ProjectName.ToLower().Contains(s.ToLower())))
                return false;

            LogProjectMessage("Skipping project due to ignore pattern: " + ProjectName);
            return true;

        }

        private bool IsAnyOutputDuplicated(IList<string> outputFiles)
        {
            var duplicatesDetector = new HashSet<string>();
            // If any output filename is duplicated, the duplicates won't get added to a hashset
            if (outputFiles.All(file => duplicatesDetector.Add(Path.GetFileName(file))))
                return false;

            Log.LogWarning("Cache cannot be used with duplicate output files:");
            var duplicates = outputFiles.Where(file => !duplicatesDetector.Add(Path.GetFileName(file))).ToList();
            duplicates.ForEach(x => Log.LogWarning($"\t{x}"));
            return true;
        }

        protected void LogProjectMessage(string message, MessageImportance importance = MessageImportance.High)
        {
            Log.LogMessage(importance, $"{ProjectName}: {message}");
        }

        protected abstract string GetCacheType();

        // We use a separate cache for each major version so we don't have to worry about version info.
        // That is, a cached file from any branch in this major version is ok to use.
        // TODO Rename Branch to Release
        protected string BranchCacheLocation => CacheLocation;

        protected string PrecomputedCacheLocation => Path.Combine(CacheLocation, "Precomputed");

        // Copy the built output files to the appropriate cache directory based on the input hash.
        protected void CacheBuildArtifacts(IList<string> inputFiles, IList<string> outputFiles, string cacheHash)
        {
            if (ShouldSkipCache(outputFiles))
                return;

            var cacheOutput = Path.Combine(BranchCacheLocation, cacheHash);
            if (string.IsNullOrWhiteSpace(cacheOutput)) return;
            LogProjectMessage("Caching artifacts to " + cacheOutput);
            if (Directory.Exists(cacheOutput))
            {
                LogProjectMessage("Removing existing cache dir");
                var retries = 10;
                while (true)
                {
                    try
                    {
                        Directory.Delete(cacheOutput, true);
                        break;
                    }
                    catch (Exception)
                    {
                        Log.LogWarning("Cannot delete " + cacheOutput);
                        if (--retries <= 10)
                        {
                            Log.LogWarning("Aborting cache");
                            return;
                        }

                        Log.LogWarning("Will retry");
                        Thread.Sleep(1000);
                    }
                }
            }
            Directory.CreateDirectory(cacheOutput);

            var interopFiles = GetInputInteropFiles(inputFiles);

            foreach (var outputFile in outputFiles)
            {
                LogProjectMessage("\t" + outputFile);
                var dst = Path.Combine(cacheOutput, Path.GetFileName(outputFile));
                CopyWithRetry(outputFile, dst);
                if (ShowContentHashes)
                {
                    LogProjectMessage($"Hash of {dst}:");
                    GetHashForFile(dst);
                }

                var outputDir = Path.GetDirectoryName(outputFile);
                if (string.IsNullOrEmpty(outputDir))
                    continue;

                // If this output file's directory contains a COM interop file, cache
                // that as well, because they seem to change with every build.
                CopyInteropFiles(interopFiles, outputDir, cacheOutput);
            }

            var branchName = Environment.GetEnvironmentVariable("CURRENT_BRANCH");
            if (branchName != null)
            {
                var branchNameDst = Path.Combine(cacheOutput, "original_branch_name.txt");
                Log.LogMessage($"Writing branchname {branchName} to {branchNameDst}");
                File.WriteAllText(branchNameDst, branchName);
            }

            var completeMarker = Path.Combine(cacheOutput, "complete");
            File.Create(completeMarker).Close();
        }

        private static List<string> GetInputInteropFiles(IList<string> inputFiles)
        {
            var interopFiles = inputFiles
                .Where(IsInteropFile)
                .Select(Path.GetFileName)
                .ToList();
            return interopFiles;
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

            var inputHash = GetHashForFiles(inputFiles);
            inputHash = GetHashForContent(inputHash + BuildConfig);
            var cacheOutput = GetExistingCacheDirForHash(inputHash);

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
                    var outputFilePath = Path.GetFullPath(outputFile);
                    LogProjectMessage($"\t\t\t{outputFilePath}", MessageImportance.Normal);
                    if (ShowContentHashes)
                    {
                        GetHashForFile(outputFilePath);
                    }
                }

                LogProjectMessage("\t\tOne or more inputs may have changed.", recompileReasonPriority);
                var outputFileInfos = outputFiles.Select(x => new FileInfo(x)).ToList();
                var oldestOutputFile = outputFileInfos.OrderBy(x => x.LastWriteTime).First();
                var oldestOutputTime = oldestOutputFile.LastWriteTime;
                LogProjectMessage("\t\tOldest output file is " + oldestOutputFile);

                var hasInputChanged = false;
                foreach (var inputFile in inputFiles)
                {
                    var fi = new FileInfo(inputFile);
                    if (fi.LastWriteTime < oldestOutputTime) continue;
                    LogProjectMessage("\t\t\tChanged input: " + Path.GetFullPath(inputFile), recompileReasonPriority);
                    hasInputChanged = true;
                    LogProjectMessage("\t\t\tNot checking for any more changed inputs", MessageImportance.Normal);
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

            if (string.IsNullOrWhiteSpace(cacheOutput))
                return inputHash;

            var interopFiles = GetInputInteropFiles(inputFiles);

            foreach (var outputFile in outputFiles)
            {
                var outputFileFull = Path.GetFullPath(outputFile);
                LogProjectMessage("\t" + outputFileFull, MessageImportance.Normal);
                var filename = Path.GetFileName(outputFileFull);
                // ReSharper disable once AssignNullToNotNullAttribute
                var src = Path.Combine(cacheOutput, filename);
                if (!File.Exists(src))
                {
                    LogProjectMessage($"\t\tCache file {filename} missing, recompiling...");
                    return inputHash;
                }

                LogProjectMessage("Restoring cached file to " + outputFileFull);
                var outputDir = Path.GetDirectoryName(outputFileFull);
                if (string.IsNullOrWhiteSpace(outputDir))
                {
                    throw new Exception($"Cannot parse directory of file {outputFileFull}");
                }
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                if (ShowContentHashes)
                {
                    GetHashForFile(src);
                }
                CopyWithRetry(src, outputFileFull);
                TouchFileWithRetry(outputFileFull, DateTime.UtcNow);

                // Copy any COM interop DLLs to all output folders, as we don't know where
                // they need to go.
                CopyInteropFiles(interopFiles, cacheOutput, outputDir);
            }

            // If source mapping file exist in cache, copy to primary output directory
            var mapFile = Directory.GetFiles(cacheOutput, "*.mapped").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(mapFile))
            {
                LogProjectMessage("Map file found: " + mapFile, MessageImportance.Low);
                var primaryOutput = outputFiles.FirstOrDefault();
                var outDir = Path.GetDirectoryName(primaryOutput) ?? "";
                LogProjectMessage("Primary output dir is " + outDir);
                var mapFileDest = Path.Combine(outDir, Path.GetFileName(mapFile));

                CopyWithRetry(mapFile, mapFileDest);
            }

            var originalBranchFile = Path.Combine(cacheOutput, "original_branch_name.txt");
            if (File.Exists(originalBranchFile))
            {
                LogProjectMessage($"Reading original branch name from {originalBranchFile}",
                    MessageImportance.Low);
                var originalBranchName = File.ReadAllText(originalBranchFile);
                LogProjectMessage($"Original branch: {originalBranchName}");
            }
            else
            {
                LogProjectMessage("Original branch name not found at " + originalBranchFile);
            }

            restoreSuccessful = true;
            return inputHash;
        }

        private void CopyInteropFiles(IEnumerable<string> interopFiles, string srcDir, string destDir)
        {
            foreach (var file in interopFiles)
            {
                var possibleInteropLocation = Path.Combine(srcDir, file);
                LogProjectMessage($"Checking for possible interop {possibleInteropLocation}");
                if (!File.Exists(possibleInteropLocation))
                    continue;

                LogProjectMessage($"Copying interop {possibleInteropLocation} -> {destDir}");
                var interopDestination = Path.Combine(destDir, file);
                CopyWithRetry(possibleInteropLocation, interopDestination);
            }
        }

        private void CopyWithRetry(string src, string dst)
        {
            var retries = 600;
            while (true)
            {
                try
                {
                    if (File.Exists(dst))
                    {
                        LogProjectMessage("Deleting existing file " + dst);
                        File.Delete(dst);
                    }

                    LogProjectMessage("Copying " + src + " to " + dst);
                    File.Copy(src, dst);
                    return;
                }
                catch (Exception e)
                {
                    if (!(e is IOException || e is UnauthorizedAccessException))
                        throw;

                    Log.LogWarning("Error writing to " + dst);
                    Log.LogWarning(e.Message);
                    if (--retries <= 0)
                    {
                        Log.LogError("Stopping build");
                        throw;
                    }

                    Log.LogWarning("Will retry");
                    Thread.Sleep(1000);
                }
            }
        }

        protected void TouchFileWithRetry(string file, DateTime newTimeUTC)
        {
            var retries = 600;
            while (true)
            {
                try
                {
                    File.SetLastWriteTimeUtc(file, newTimeUTC);
                    return;
                }
                catch (Exception e)
                {
                    if (!(e is IOException || e is UnauthorizedAccessException))
                        throw;

                    Log.LogWarning("Error touching " + file);
                    Log.LogWarning(e.Message);
                    if (--retries <= 0)
                    {
                        Log.LogError("Stopping build");
                        throw;
                    }

                    Log.LogWarning("Will retry");
                    Thread.Sleep(1000);
                }
            }
        }

        // Content hash is the hash of each input file's content, concatenanted then rehashed
        protected string GetHashForFiles(IList<string> files)
        {
            var sb = new StringBuilder(48 * (files.Count + 5));
            var priority = ShowContentHashes ? MessageImportance.High : MessageImportance.Low;
            LogProjectMessage($"Generating hashes for {files.Count} files", priority);
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

            if (IsInteropFile(file))
            {
                return "inerop";
            }

            var bytes = GetBytesWithRetry(file);
            if (!IsVersionableFile(file))
            {
                return GetHashForContent(bytes);
            }

            // TODO Firgure out actual encoding
            var content = Encoding.UTF8.GetString(bytes).Split(new[] { "\r\n", "\n" },
                StringSplitOptions.None);
            var filteredContent = new List<string>();
            foreach (var line in content)
            {
                var lineAsLower = line.ToLower();
                if (GetListOfVersionKeywordsToIgnore().Any(key => lineAsLower.Contains(key)))
                    continue;
                filteredContent.Add(line);
            }

            return GetHashForContent(string.Join(Environment.NewLine, filteredContent));
        }

        private static bool IsInteropFile(string file)
        {
            var filenameLower = Path.GetFileName(file)?.ToLower();
            if (filenameLower == null)
                return false;
            return filenameLower.StartsWith("interop.") && filenameLower.EndsWith(".dll");
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

            // Used to ensure we recompute if the source file is modified
            TouchFileWithRetry(newHashFile, fi.LastWriteTimeUtc);

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
            try
            {
                Directory.SetLastWriteTimeUtc(cacheOutput, DateTime.UtcNow);
            }
            catch (Exception)
            {
                Log.LogError("Cannot update cache timestamp on " + cacheOutput);
            }
            return cacheOutput;
        }

        protected void WriteSourceMapFile(IList<string> inputs, IList<string> outputs, string WorkDir)
        {
            if (string.IsNullOrWhiteSpace(RootDir))
                return;

            var ignoredOutputs = new[] { ".pdb", ".map", ".cs" };
            var rootDirSub = RootDir.ToLower();
            if (!rootDirSub.EndsWith("\\"))
                rootDirSub += "\\";

            if (inputs.Count == 0 || outputs.Count == 0)
                return;

            // TODO Remove NuGet packages and fallback directory
            var primaryOutput = outputs.FirstOrDefault();
            var outDir = Path.GetDirectoryName(primaryOutput) ?? "";
            LogProjectMessage("Primary output dir is " + outDir);

            var mapFile = Path.Combine(outDir, Guid.NewGuid() + ".mapped");

            var generalInputs = inputs.Select(x => Path.Combine(WorkDir, x)).Select(y => y.ToLower().Replace(rootDirSub, "")).Select(z => "INP:" + z);
            var generalOutputs = outputs.Where(y => !ignoredOutputs.Contains(Path.GetExtension(y).ToLower())).Select(x => Regex.Replace(x, @".*\\", "")).Select(z => "OUT:" + z);

            while (true)
            {
                try
                {
                    File.WriteAllLines(mapFile, generalInputs);
                    File.AppendAllLines(mapFile, generalOutputs);
                    break;
                }
                catch (Exception e)
                {
                    if (!(e is IOException || e is UnauthorizedAccessException))
                        throw;

                    Log.LogWarning("Error writing to " + mapFile + ", will retry:");
                    Log.LogWarning(e.Message);
                    Thread.Sleep(1000);
                }
            }
            outputs.Add(mapFile);
        }

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private const long ERROR_LOCK_VIOLATION = 0x21;

        private string CacheLocation => Path.Combine(CacheRoot, GetCacheType());

        private bool IsVersionableFile(string file)
        {
            var filename = Path.GetFileName(file).ToLower();
            return filename.EndsWith("assemblyinfo.cs")
                   || filename.EndsWith(".csproj")
                   || filename.EndsWith(".rc")
                   || filename.EndsWith(".rc2");
        }

        private IEnumerable<string> GetListOfVersionKeywordsToIgnore()
        {
            if (_versionKeywordsToIgnore == null)
            {
                _versionKeywordsToIgnore = new List<string>
                {
                    "AssemblyVersion",
                    "AssemblyFileVersion",
                    "AssemblyInformationalversion",
                    "AssemblyCompany",
                    "AssemblyCopyright",
                    "FILEVERSION",
                    "PRODUCTVERSION",
                    "CompanyName",
                    "LegalCopyright",
                    "<FileVersion>",
                    "<Version>",
                    "<AssemblyVersion>",
                    "<InformationalVersion>"
                }.Select(x => x.ToLower()).ToList();

            }
            return _versionKeywordsToIgnore;
        }
    }
}