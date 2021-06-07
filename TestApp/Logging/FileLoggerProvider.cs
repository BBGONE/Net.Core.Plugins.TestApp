using Extensions.Logging.RollingFile.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Extensions.Logging.RollingFile
{
    [ProviderAlias("File")]
    public class FileLoggerProvider : BatchingLoggerProvider
    {
        private readonly byte[] _bufferPool = new byte[64 * 1024];
        private readonly string _path;
        private readonly string _fileName;
        private readonly int? _maxFileSize;
        private readonly int? _maxRetainedFiles;
        private readonly Dictionary<(int Year, int Month, int Day), (int Num, DateTime LastAccess)> _fileNumbers = new Dictionary<(int Year, int Month, int Day), (int Num, DateTime LastAccess)>();

        public FileLoggerProvider(IOptions<FileLoggerOptions> options) : base(options)
        {
            var loggerOptions = options.Value;
            _path = loggerOptions.LogDirectory;
            _fileName = loggerOptions.FileName;
            _maxFileSize = loggerOptions.FileSizeLimit;
            _maxRetainedFiles = loggerOptions.RetainedFileCountLimit;
        }

        /// <summary>
        /// Check the maximum capacity and remove half of it if it exceeds the limit
        /// </summary>
        /// <param name="countLimit"></param>
        private void _CleanUpFileNumbers(int countLimit = 64)
        {
            if (_fileNumbers.Count >= countLimit)
            {
                // remove half the values - oldest first
                var removalList = _fileNumbers.OrderBy(v => v.Value.LastAccess).Take(countLimit / 2).ToArray();
                foreach (var kv in removalList)
                {
                    _fileNumbers.Remove(kv.Key);
                }
            }
        }

        private (string fullName, bool isRollNeeded, long fileSize) _GetFileInfo((int Year, int Month, int Day) key, bool forceNewFile)
        {
            Func<long, bool> IsFull = (size) => _maxFileSize > 0 && size > 0 && size > _maxFileSize;
            bool isRollNeeded = false;

            if (!_fileNumbers.ContainsKey(key))
            {
                // We need try to find the last file in the group (in case the application restarted)
                // or else we can start with zero even if higher number exists
                DirectoryInfo dirInfo = new DirectoryInfo(_path);
                string searchPattern = GetSearchPattern(key);
                var lastFileInfo = dirInfo.EnumerateFiles(searchPattern).OrderByDescending(f => f.Name).FirstOrDefault();
                int lastNumber = 0;
                try
                {
                    lastNumber = lastFileInfo == null ? 0 : int.Parse(GetFileNumber(lastFileInfo.Name));
                }
                catch
                {
                    // ignore
                }

                _fileNumbers.Add(key, (lastNumber, DateTime.Now));
            }

            var currentFile = _fileNumbers[key];
            var fullName = GetFullName(key, currentFile.Num);
            var fileInfo = new FileInfo(fullName);
            long fileSize = fileInfo.Exists ? fileInfo.Length : 0;

            if (forceNewFile || IsFull(fileSize))
            {
                isRollNeeded = true;

                bool found = false;
                for (int i = currentFile.Num + 1; i < 100000; ++i)
                {
                    fullName = GetFullName(key, i);
                    fileInfo = new FileInfo(fullName);
                    fileSize = fileInfo.Exists ? fileInfo.Length : 0;
                    if (fileSize == 0 || !IsFull(fileSize))
                    {
                        found = true;
                        // update last file number for the group
                        _fileNumbers[key] = (i, DateTime.Now);
                        break;
                    }
                }

                // we went through all 100,000 numbers! 
                // this could not happen because files are rolled (just in case)
                if (!found)
                {
                    fullName = string.Empty;
                }
            }
            else
            {
                // save last access time
                _fileNumbers[key] = (currentFile.Num, DateTime.Now);
            }

            return (fullName, isRollNeeded, fileSize);
        }

        protected override void WriteMessages(IEnumerable<LogMessage> messages, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_path);

            _CleanUpFileNumbers();

            foreach (var group in messages.GroupBy(GetGrouping))
            {
                (var fullName, var isRollNeeded, var fileSize) = _GetFileInfo(group.Key, false);

                if (string.IsNullOrEmpty(fullName))
                {
                    return;
                }

                Stream fileStream = GetFileStream(fullName);
                try
                {
                    if (isRollNeeded)
                    {
                        RollFiles();
                    }

                    foreach (var item in group)
                    {
                        using (var memStream = new MemoryStream(_bufferPool, 0, _bufferPool.Length, true, true))
                        using (var streamWriter = new StreamWriter(memStream, System.Text.Encoding.UTF8, 4096, true))
                        {
                            int dataLength = 0;
                            try
                            {
                                streamWriter.Write(item.Message);
                                streamWriter.WriteLine();
                                streamWriter.Flush();
                                dataLength = (int)memStream.Position;
                            }
                            catch
                            {
                                // ignore
                            }

                            // this can happen if the message exceeds MemoryStream's buffer size (64 KB)
                            if (dataLength == 0)
                            {
                                continue;
                            }

                            fileSize += dataLength;

                            if (_maxFileSize > 0 && fileSize > _maxFileSize)
                            {
                                fileStream.Close();
                                (fullName, isRollNeeded, fileSize) = _GetFileInfo(group.Key, true);

                                if (string.IsNullOrEmpty(fullName))
                                {
                                    return;
                                }

                                fileSize += dataLength;
                                fileStream = GetFileStream(fullName);
                                RollFiles();
                            }


                            fileStream.Write(memStream.GetBuffer(), 0, dataLength);
                        }
                    }
                }
                finally
                {
                    fileStream.Close();
                }
            }
        }

        private static Stream GetFileStream(string fullName)
        {
            var fileStream = new FileStream(fullName, FileMode.Append, FileAccess.Write, FileShare.Read, 4 * 1024, false);
            try
            {
                return new BufferedStream(fileStream, 4 * 1024);
            }
            catch
            {
                fileStream.Dispose();
                throw;
            }
        }

        private static string GetFileNumber(string fileName)
        {
            // take the last file part including file's extension
            string nameLastPart = fileName.Substring(fileName.Length - 9);
            // take only the number part without extension
            return nameLastPart.Substring(0, 5);
        }

        private string GetSearchPattern((int Year, int Month, int Day) group)
        {
            return $"{_fileName}{group.Year:0000}{group.Month:00}{group.Day:00}_*.txt";
        }

        private string GetFullName((int Year, int Month, int Day) group, int num = 0)
        {
            return Path.Combine(_path, $"{_fileName}{group.Year:0000}{group.Month:00}{group.Day:00}_{num:00000}.txt");
        }

        private (int Year, int Month, int Day) GetGrouping(LogMessage message)
        {
            return (message.Timestamp.Year, message.Timestamp.Month, message.Timestamp.Day);
        }

        protected void RollFiles()
        {
            if (_maxRetainedFiles > 0)
            {
                var files = new DirectoryInfo(_path)
                    .GetFiles(_fileName + "*")
                    .OrderByDescending(f => f.Name)
                    .Skip(_maxRetainedFiles.Value);

                foreach (var item in files)
                {
                    try
                    {
                        // maybe somebody opened it
                        item.Delete();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
    }
}
