using System;
using System.Collections.Generic;
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
        public string Inputs { get; set; }


        [Required]
        public string Outputs { get; set; }

        protected IList<string> ParseFileList(string raw)
        {
            return raw.Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && !x.Contains("AssemblyInfo.cs")).Select(
                y => y.Trim()).OrderBy(z => z).Distinct().ToList();
        }

        protected string GetContentHash(IEnumerable<string> files)
        {
            var sb = new StringBuilder();
            foreach (var file in files)
            {
                Log.LogMessage(MessageImportance.High, "\t\t\tInput: " + file);
                using (var fs = OpenFileWithRetry(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var textReader = new StreamReader(fs))
                    {
                        sb.Append(textReader.ReadToEnd());
                        Log.LogMessage(MessageImportance.High, 
                            string.Format("\t\t\t\tLength: {0}", sb.Length));

                    }
                }
            }

            using (var cryptoProvider = new SHA1CryptoServiceProvider())
            {
                var hashString = BitConverter
                        .ToString(cryptoProvider.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
                hashString = hashString.Replace("-", "");
                Log.LogMessage(MessageImportance.High, "\t\t\t\tHash: " + hashString);
                return hashString;
            }
        }

        protected bool ShouldSkipCache(IEnumerable<string> outputFiles)
        {
            var hashset = new HashSet<string>();
            if (outputFiles.Any(file => !hashset.Add(Path.GetFileName(file))))
            {
                Log.LogMessage(MessageImportance.High, "Cache cannot be used with duplicate output files");
                return true;
            }

            return false;
        }

        protected string CacheLocation = @"c:\temp\minbuild";

        private FileStream OpenFileWithRetry(string path, FileMode mode, FileAccess fileAccess, FileShare fileShare)
        {
            var autoResetEvent = new AutoResetEvent(false);

            while (true)
            {
                try
                {
                    return new FileStream(path, mode, fileAccess, fileShare);
                }
                catch (IOException)
                {
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

        private const long ERROR_SHARING_VIOLATION = 0x20;
        private long ERROR_LOCK_VIOLATION = 0x21;
    }
}