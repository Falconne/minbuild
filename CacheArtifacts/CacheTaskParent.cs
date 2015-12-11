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
        public string Inputs { get; set; }


        [Required]
        public string Outputs { get; set; }

        protected IList<string> ParseFileList(string raw)
        {
            return raw.Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && !x.Contains("AssemblyInfo.cs")).Select(
                y => y.Trim()).OrderBy(z => z).Distinct().ToList();
        }

        protected string GetFilenameHash(IEnumerable<string> files)
        {
            var composite = new StringBuilder();
            foreach (var file in files)
            {
                composite.Append(file);
                using (var md5Hash = MD5.Create())
                {
                    var hash = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(composite.ToString()));
                    var sb = new StringBuilder();
                    foreach (var t in hash)
                    {
                        sb.Append(t.ToString("X2"));
                    }

                    var hashString = sb.ToString();
                    return hashString;
                }
            }

            return null;
        }

        protected string GetContentHash(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                using (var fs = OpenFileWithRetry(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var bs = new BufferedStream(fs))
                {
                    using (var cryptoProvider = new SHA1CryptoServiceProvider())
                    {
                        var hashString = BitConverter
                                .ToString(cryptoProvider.ComputeHash(bs));
                        hashString = hashString.Replace("-", "");
                        return hashString;
                    }
                }
            }

            return null;
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
                catch (IOException ex)
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