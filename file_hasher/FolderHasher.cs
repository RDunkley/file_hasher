// ******************************************************************************************************************************
// Copyright © Richard Dunkley 2025
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// ******************************************************************************************************************************
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace file_hasher
{
	/// <summary>
	///   Hashes all files and subfolders in a specified folder, and groups them by their hash values.
	/// </summary>
	public class FolderHasher
	{
		#region Fields

		/// <summary>
		///   Algorithm to use for hashing the files.
		/// </summary>
		private HashAlgorithm _algo;

		/// <summary>
		///   True to display the hash in hexadecimal format, false to display in base64.
		/// </summary>
		private bool _dispInHex = false;

		/// <summary>
		///   Tracks the files by their hash values, where the key is the hash and the value is the file path.
		/// </summary>
		/// <remarks>Only used when <see cref="TrackDuplicates"/> is true.</remarks>
		private Dictionary<string, string> _fileByHash = new Dictionary<string, string>();

		/// <summary>
		///   Actual folder paths queued or already scanned, used to avoid following symlink cycles.
		/// </summary>
		private ConcurrentDictionary<string, byte> _queuedFolders = new ConcurrentDictionary<string, byte>(GetPathComparer());

		/// <summary>
		///   Files discovered during the scan. Key is the path opened for hashing; value is the displayed path.
		/// </summary>
		private ConcurrentDictionary<string, string> _scannedFiles = new ConcurrentDictionary<string, string>();

		/// <summary>
		///   Interval used by the main thread when refreshing scan and hash progress on the console.
		/// </summary>
		private const int ProgressUpdateIntervalMs = 250;

		/// <summary>
		///   Length of the current in-place progress line, used to pad and overwrite it.
		/// </summary>
		private int _progressLineLength;

		/// <summary>
		///   Number of threads to use when scanning and hashing. Values less than 1 are treated as 1.
		/// </summary>
		private int _threadCount = 1;

		/// <summary>
		///   Protects shared scan and hash lookup collections.
		/// </summary>
		private readonly object _sync = new object();

		/// <summary>
		///   Protects in-place console progress and status messages.
		/// </summary>
		private readonly object _consoleLock = new object();

		#endregion

		#region Properties

		/// <summary>
		///   Lookup table of duplicate files, where the key is the file that is a duplicate of the corresponding value. Only valid if <see cref="TrackDuplicates"/> is true.
		/// </summary>
		public Dictionary<string, string> DuplicateFiles { get; private set; } = null;

		/// <summary>
		///   Lookup table of file paths, where the key is the actual file path (not the symbolic link) and the value is the hash of that file.
		/// </summary>
		public Dictionary<string, string> HashByFile { get; private set; } = new Dictionary<string, string>();

		/// <summary>
		///   True to track duplicate files, false to ignore them.
		/// </summary>
		public bool TrackDuplicates { get; private set; }

		/// <summary>
		///   Display the file and hash to the console output while processing the files.
		/// </summary>
		public bool DisplayWhileProcessing { get; set; }

		/// <summary>
		///   Display an error message to the console output if a file or folder could not be accessed.
		/// </summary>
		public bool DisplayErrorOnAccess { get; set; }

		/// <summary>
		///   True to process symbolic links as if they were normal files. If false, symbolic links to files and folders are ignored.
		/// </summary>
		public bool ProcessSymLinks { get; set; }

		/// <summary>
		///   True to also hash files contained in zip and gz archives by decompressing them on the fly.
		/// </summary>
		public bool ProcessCompressed { get; set; }

		/// <summary>
		///   Number of threads to use when scanning and hashing. Defaults to 1.
		/// </summary>
		public int ThreadCount
		{
			get { return _threadCount; }
			set { _threadCount = value < 1 ? 1 : value; }
		}

		#endregion

		#region Methods

		/// <summary>
		///   Instantiates a new <see cref="FolderHasher"/> with the specified hash algorithm.
		/// </summary>
		/// <param name="algorithm"><see cref="HashAlgorithm"/> to be used.</param>
		/// <param name="dispInHex">True to displays the hash in hexadecimal format. False to display in base64.</param>
		/// <param name="trackDuplicates">True to track duplicate files, false to ignore them.</param>
		/// <exception cref="ArgumentNullException"><paramref name="algorithm"/> is null.</exception>
		public FolderHasher(HashAlgorithm algorithm, bool dispInHex, bool trackDuplicates = false)
		{
			if(algorithm == null)
				throw new ArgumentNullException(nameof(algorithm));

			_algo = algorithm;
			_dispInHex = dispInHex;
			TrackDuplicates = trackDuplicates;

			if(TrackDuplicates)
				DuplicateFiles = new Dictionary<string, string>();
		}

		/// <summary>
		///   Adds the specified folder's subfolders and files to the hash lookup tables.
		/// </summary>
		/// <param name="folder">Folder to be hashed.</param>
		/// <param name="cancel"><see cref="CancellationToken"/> to cancel the hashing.</param>
		/// <exception cref="ArgumentException"><paramref name="folder"/> does not exist.</exception>
		/// <exception cref="OperationCanceledException">Thrown if the operation is cancelled.</exception>
		public void Hash(string folder, CancellationToken? cancel)
		{
			Hash(new string[] { folder }, cancel);
		}

		/// <summary>
		///   Scans the specified folders for files, then hashes each discovered file from that list.
		/// </summary>
		/// <param name="folders">Folders to be hashed.</param>
		/// <param name="cancel"><see cref="CancellationToken"/> to cancel the hashing.</param>
		/// <exception cref="ArgumentException">A specified folder does not exist.</exception>
		/// <exception cref="OperationCanceledException">Thrown if the operation is cancelled.</exception>
		public void Hash(IEnumerable<string> folders, CancellationToken? cancel)
		{
			_scannedFiles.Clear();
			_queuedFolders.Clear();
			_progressLineLength = 0;

			Console.WriteLine("Scanning for Files...");
			WriteScanProgress();
			ScanFolders(folders, cancel);
			WriteScanProgress();
			FinishProgressLine();

			bool displayHashes = DisplayWhileProcessing;
			DisplayWhileProcessing = false; // Progress line owns the console during hashing.
			try
			{
				HashScannedFiles(cancel);
			}
			finally
			{
				DisplayWhileProcessing = displayHashes;
				FinishProgressLine();
			}

			if (displayHashes)
			{
				foreach (var pair in HashByFile)
					Console.WriteLine($"{pair.Key} -> {pair.Value}");
			}
		}

		/// <summary>
		///   Clears the hash lookup tables and skipped files.
		/// </summary>
		public void Clear()
		{
			DuplicateFiles?.Clear();
			HashByFile.Clear();
			_scannedFiles.Clear();
			_queuedFolders.Clear();
		}

		/// <summary>
		///   Scans the specified folders using worker threads that post discovered files into <see cref="_scannedFiles"/>.
		///   The calling thread polls that collection to update the files-found count.
		/// </summary>
		/// <param name="folders">Root folders to scan.</param>
		/// <param name="cancel"><see cref="CancellationToken"/> to cancel the scan.</param>
		private void ScanFolders(IEnumerable<string> folders, CancellationToken? cancel)
		{
			var queue = new BlockingCollection<string>();
			int outstanding = 0;

			foreach (string folder in folders)
			{
				if (!Directory.Exists(folder))
					throw new ArgumentException($"The folder specified ({folder}) does not exist.");

				if (TryQueueFolder(folder, out string pathToScan))
				{
					queue.Add(pathToScan);
					Interlocked.Increment(ref outstanding);
				}
			}

			if (outstanding == 0)
			{
				queue.CompleteAdding();
				return;
			}

			var workers = new Task[_threadCount];
			for (int i = 0; i < _threadCount; i++)
			{
				workers[i] = Task.Run(() =>
				{
					foreach (string folder in queue.GetConsumingEnumerable())
					{
						try
						{
							if (cancel.HasValue && cancel.Value.IsCancellationRequested)
								throw new OperationCanceledException();

							string[] children = ScanFolderContents(folder, cancel);
							if (children != null)
							{
								foreach (string child in children)
								{
									if (TryQueueFolder(child, out string pathToScan))
									{
										Interlocked.Increment(ref outstanding);
										queue.Add(pathToScan);
									}
								}
							}
						}
						catch (OperationCanceledException)
						{
							queue.CompleteAdding();
							throw;
						}
						catch (InvalidOperationException)
						{
							// Adding completed while this worker was still enumerating children.
						}
						finally
						{
							if (Interlocked.Decrement(ref outstanding) == 0)
								queue.CompleteAdding();
						}
					}
				});
			}

			WaitForWorkers(workers, WriteScanProgress);
		}

		/// <summary>
		///   Hashes files by having worker threads pull entries from <see cref="_scannedFiles"/> until it is empty.
		///   The calling thread polls the remaining count every <see cref="ProgressUpdateIntervalMs"/> milliseconds.
		/// </summary>
		/// <param name="cancel"><see cref="CancellationToken"/> to cancel the hashing.</param>
		private void HashScannedFiles(CancellationToken? cancel)
		{
			int total = _scannedFiles.Count;
			WriteHashProgress(total);

			if (total == 0)
				return;

			var workers = new Task[_threadCount];
			for (int i = 0; i < _threadCount; i++)
			{
				workers[i] = Task.Run(() =>
				{
					using (HashAlgorithm algo = CreateHashAlgorithm())
					{
						while (true)
						{
							if (cancel.HasValue && cancel.Value.IsCancellationRequested)
								throw new OperationCanceledException();

							if (!TryTakeScannedFile(out string srcFile, out string file))
							{
								if (_scannedFiles.IsEmpty)
									return;

								Thread.Yield();
								continue;
							}

							HashFile(file, srcFile, algo);
						}
					}
				});
			}

			WaitForWorkers(workers, () => WriteHashProgress(total));
			WriteHashProgress(total);
		}

		/// <summary>
		///   Tries to take one file from <see cref="_scannedFiles"/> for hashing.
		/// </summary>
		/// <param name="srcFile">Path to open and hash.</param>
		/// <param name="file">Displayed path of the file.</param>
		/// <returns>True if a file was taken; false if none were available.</returns>
		private bool TryTakeScannedFile(out string srcFile, out string file)
		{
			foreach (KeyValuePair<string, string> pair in _scannedFiles)
			{
				if (_scannedFiles.TryRemove(pair.Key, out file))
				{
					srcFile = pair.Key;
					return true;
				}
			}

			srcFile = null;
			file = null;
			return false;
		}

		/// <summary>
		///   Waits for worker tasks, invoking <paramref name="reportProgress"/> about every
		///   <see cref="ProgressUpdateIntervalMs"/> milliseconds until they complete.
		/// </summary>
		/// <param name="workers">Worker tasks to wait on.</param>
		/// <param name="reportProgress">Callback used to refresh the console progress line.</param>
		private static void WaitForWorkers(Task[] workers, Action reportProgress)
		{
			while (true)
			{
				bool completed;
				try
				{
					completed = Task.WaitAll(workers, ProgressUpdateIntervalMs);
				}
				catch (AggregateException e)
				{
					Exception first = e.Flatten().InnerExceptions.FirstOrDefault(inner => inner is not InvalidOperationException);
					if (first != null)
						throw first;
					break;
				}

				reportProgress();
				if (completed)
					break;
			}
		}
		/// <summary>
		///   Lists files in <paramref name="folder"/> and posts them into <see cref="_scannedFiles"/>.
		/// </summary>
		/// <param name="folder">Folder to scan.</param>
		/// <param name="cancel"><see cref="CancellationToken"/> to cancel the scan.</param>
		/// <returns>Child directories to scan, or null if this folder should be skipped.</returns>
		private string[] ScanFolderContents(string folder, CancellationToken? cancel)
		{
			if (cancel.HasValue && cancel.Value.IsCancellationRequested)
				throw new OperationCanceledException();

			if(!ProcessSymLinks)
			{
				// Ignore symbolic links to folders.
				var dirInfo = new DirectoryInfo(folder);
				if(dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
					return null;
			}

			string[] files;
			try
			{
				files = Directory.GetFiles(folder);
			}
			catch(Exception e) when (e is UnauthorizedAccessException || e is SecurityException)
			{
				if(DisplayErrorOnAccess)
					WriteConsoleMessage($"{folder} -> {e.Message}");
				return null;
			}

			foreach (string file in files)
			{
				if (cancel.HasValue && cancel.Value.IsCancellationRequested)
					throw new OperationCanceledException();

				string srcFile = file; // Store the original file path to use in case of symbolic links.
				var fileInfo = new FileInfo(file);
				if (!ProcessSymLinks)
				{
					// Ignore symbolic links to files.
					if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
						continue;
				}
				else
				{
					if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
						srcFile = File.ResolveLinkTarget(file, true).FullName;
				}

				_scannedFiles.TryAdd(srcFile, file);
			}

			string[] children;
			try
			{
				children = Directory.GetDirectories(folder);
			}
			catch(Exception e) when (e is UnauthorizedAccessException || e is SecurityException)
			{
				if(DisplayErrorOnAccess)
					WriteConsoleMessage($"{folder} -> {e.Message}");
				return null;
			}

			return children;
		}

		/// <summary>
		///   Queues a folder for scanning if its actual path has not already been queued.
		/// </summary>
		/// <remarks>
		///   When <see cref="ProcessSymLinks"/> is true, the path is resolved to the real folder so a child
		///   symlink that points at a parent (or any folder already scanned) is skipped.
		/// </remarks>
		/// <param name="folder">Folder path as discovered (may be a symbolic link).</param>
		/// <param name="pathToScan">Actual folder path to scan when this method returns true.</param>
		/// <returns>True if the folder was newly queued; false if it should be skipped.</returns>
		private bool TryQueueFolder(string folder, out string pathToScan)
		{
			pathToScan = folder;
			if (ProcessSymLinks)
			{
				pathToScan = GetActualFolderPath(folder);
				if (pathToScan == null)
					return false;
			}

			return _queuedFolders.TryAdd(pathToScan, 0);
		}

		/// <summary>
		///   Resolves <paramref name="folder"/> to a normalized actual directory path, following symbolic links.
		/// </summary>
		/// <param name="folder">Folder path that may be a symbolic link.</param>
		/// <returns>Normalized full path of the target folder, or null if it cannot be resolved.</returns>
		private static string GetActualFolderPath(string folder)
		{
			try
			{
				string full = NormalizeFolderPath(Path.GetFullPath(folder));
				var dirInfo = new DirectoryInfo(full);
				if (!dirInfo.Exists)
					return null;

				if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					FileSystemInfo target = Directory.ResolveLinkTarget(full, true);
					if (target == null || !Directory.Exists(target.FullName))
						return null;
					full = NormalizeFolderPath(Path.GetFullPath(target.FullName));
				}

				return full;
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException || e is ArgumentException || e is NotSupportedException)
			{
				return null;
			}
		}

		/// <summary>
		///   Normalizes a folder path so equivalent locations use the same dictionary key.
		/// </summary>
		/// <param name="path">Full folder path.</param>
		/// <returns>Path without a trailing separator, except for a root path.</returns>
		private static string NormalizeFolderPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return path;

			char sep = Path.DirectorySeparatorChar;
			char alt = Path.AltDirectorySeparatorChar;
			if (path.Length > 1 && (path[path.Length - 1] == sep || path[path.Length - 1] == alt))
			{
				string root = Path.GetPathRoot(path);
				if (root == null || path.Length > root.Length)
					path = path.TrimEnd(sep, alt);
			}
			return path;
		}

		/// <summary>
		///   Path comparer used when tracking folders that have already been queued or scanned.
		/// </summary>
		private static StringComparer GetPathComparer()
		{
			return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		}

		/// <summary>
		///   Writes or updates the in-place scan count line from <see cref="_scannedFiles"/>.
		/// </summary>
		private void WriteScanProgress()
		{
			WriteInPlaceLine($"{_scannedFiles.Count} Files Found");
		}

		/// <summary>
		///   Writes or updates the in-place hashing progress line from the number of files still in <see cref="_scannedFiles"/>.
		/// </summary>
		/// <param name="total">Total number of files discovered during the scan.</param>
		private void WriteHashProgress(int total)
		{
			int remaining = _scannedFiles.Count;
			int current = total - remaining;
			if (current < 0)
				current = 0;
			WriteInPlaceLine($"Hashing file {current} of {total}");
		}

		/// <summary>
		///   Overwrites the current console line with <paramref name="text"/>, padding if needed to clear leftovers.
		/// </summary>
		/// <param name="text">Text to display on the current line.</param>
		private void WriteInPlaceLine(string text)
		{
			lock (_consoleLock)
			{
				if (text.Length < _progressLineLength)
					text = text.PadRight(_progressLineLength);
				Console.Write('\r' + text);
				_progressLineLength = text.Length;
			}
		}

		/// <summary>
		///   Completes the in-place progress line so later console output starts on a new line.
		/// </summary>
		private void FinishProgressLine()
		{
			lock (_consoleLock)
			{
				if (_progressLineLength > 0)
				{
					Console.WriteLine();
					_progressLineLength = 0;
				}
			}
		}

		/// <summary>
		///   Writes a console message, moving past the hashing progress line first if it is currently displayed.
		/// </summary>
		/// <param name="message">Message to write.</param>
		private void WriteConsoleMessage(string message)
		{
			lock (_consoleLock)
			{
				if (_progressLineLength > 0)
				{
					Console.WriteLine();
					_progressLineLength = 0;
				}
				Console.WriteLine(message);
			}
		}

		/// <summary>
		///   Creates a new hash algorithm instance matching the algorithm configured for this hasher.
		/// </summary>
		/// <returns>A new <see cref="HashAlgorithm"/> instance.</returns>
		private HashAlgorithm CreateHashAlgorithm()
		{
			if (_algo is MD5)
				return MD5.Create();
			return SHA256.Create();
		}

		/// <summary>
		///   Hashes a single file and, when enabled, hashes files inside zip and gz archives without extracting them to disk.
		/// </summary>
		/// <param name="file">Displayed path of the file (the link path when a symbolic link is used).</param>
		/// <param name="srcFile">Actual file path to open and hash.</param>
		/// <param name="algorithm">Hash algorithm instance used by the current thread.</param>
		private void HashFile(string file, string srcFile, HashAlgorithm algorithm)
		{
			try
			{
				using (FileStream reader = new FileStream(srcFile, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					reader.Position = 0;
					byte[] hash = algorithm.ComputeHash(reader);
					RecordHash(file, srcFile, hash);

					if (ProcessCompressed && IsCompressedFile(srcFile))
					{
						reader.Position = 0;
						HashCompressedContents(srcFile, reader, algorithm);
					}
				}
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException || e is InvalidDataException)
			{
				if (DisplayErrorOnAccess)
				{
					if(srcFile == file)
						WriteConsoleMessage($"{file} -> {e.Message}");
					else
						WriteConsoleMessage($"{file} ({srcFile}) -> {e.Message}");
				}
			}
		}

		/// <summary>
		///   Records a computed hash in the lookup tables and optionally displays it.
		/// </summary>
		/// <param name="file">Displayed path used for duplicate tracking and console output.</param>
		/// <param name="srcFile">Canonical path used as the hash lookup key.</param>
		/// <param name="hash">Computed hash bytes.</param>
		private void RecordHash(string file, string srcFile, byte[] hash)
		{
			if (hash == null)
				return;

			string hashString = GetHashString(hash);
			lock (_sync)
			{
				if (!HashByFile.ContainsKey(srcFile))
					HashByFile.Add(srcFile, hashString);

				if (TrackDuplicates)
				{
					if (_fileByHash.ContainsKey(hashString))
					{
						if (!DuplicateFiles.ContainsKey(file))
							DuplicateFiles.Add(file, _fileByHash[hashString]);
					}
					else
					{
						_fileByHash.Add(hashString, file);
					}
				}
			}

			if (DisplayWhileProcessing)
			{
				if(srcFile == file)
					WriteConsoleMessage($"{file} -> {hashString}");
				else
					WriteConsoleMessage($"{file} ({srcFile}) -> {hashString}");
			}
		}

		/// <summary>
		///   Hashes the files contained in a zip or gz stream without writing extracted files to disk.
		/// </summary>
		/// <param name="containerPath">Path of the archive, used as a prefix for contained file paths.</param>
		/// <param name="stream">Stream positioned at the start of the archive.</param>
		/// <param name="algorithm">Hash algorithm instance used by the current thread.</param>
		private void HashCompressedContents(string containerPath, Stream stream, HashAlgorithm algorithm)
		{
			if (IsZipFile(containerPath))
				HashZipContents(containerPath, stream, algorithm);
			else if (IsGzFile(containerPath))
				HashGzContents(containerPath, stream, algorithm);
		}

		/// <summary>
		///   Hashes each file entry in a zip archive by streaming the decompressed entry data.
		/// </summary>
		/// <param name="containerPath">Path of the zip archive.</param>
		/// <param name="stream">Seekable stream of the zip archive.</param>
		/// <param name="algorithm">Hash algorithm instance used by the current thread.</param>
		private void HashZipContents(string containerPath, Stream stream, HashAlgorithm algorithm)
		{
			using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
			{
				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					if (IsZipDirectory(entry))
						continue;

					string innerPath = GetInnerPath(containerPath, entry.FullName);
					lock (_sync)
					{
						if (HashByFile.ContainsKey(innerPath))
							continue;
					}

					try
					{
						using (Stream entryStream = entry.Open())
							HashArchiveEntry(innerPath, entry.FullName, entryStream, algorithm);
					}
					catch (Exception e) when (e is IOException || e is InvalidDataException || e is InvalidOperationException || e is NotSupportedException)
					{
						if (DisplayErrorOnAccess)
							WriteConsoleMessage($"{innerPath} -> {e.Message}");
					}
				}
			}
		}

		/// <summary>
		///   Hashes the decompressed contents of a gzip stream without writing the extracted file to disk.
		/// </summary>
		/// <param name="containerPath">Path of the gzip file.</param>
		/// <param name="stream">Stream positioned at the start of the gzip file.</param>
		/// <param name="algorithm">Hash algorithm instance used by the current thread.</param>
		private void HashGzContents(string containerPath, Stream stream, HashAlgorithm algorithm)
		{
			string innerName = GetGzipOriginalFileName(stream);
			if (string.IsNullOrEmpty(innerName))
				innerName = GetDefaultGzInnerName(containerPath);
			string innerPath = GetInnerPath(containerPath, innerName);

			if (stream.CanSeek)
				stream.Position = 0;

			using (GZipStream gzip = new GZipStream(stream, CompressionMode.Decompress, true))
				HashArchiveEntry(innerPath, innerName, gzip, algorithm);
		}

		/// <summary>
		///   Hashes a decompressed archive entry and, when it is itself a zip or gz, hashes files inside it as well.
		/// </summary>
		/// <param name="innerPath">Virtual path used to identify the entry in the output.</param>
		/// <param name="entryName">File name of the entry, used to detect nested archives.</param>
		/// <param name="entryStream">Stream of the entry's decompressed (zip) or compressed (nested gz/zip bytes) data.</param>
		/// <param name="algorithm">Hash algorithm instance used by the current thread.</param>
		private void HashArchiveEntry(string innerPath, string entryName, Stream entryStream, HashAlgorithm algorithm)
		{
			if (ProcessCompressed && IsCompressedFile(entryName))
			{
				// Nested archives need to be hashed and then re-read. Buffer in memory so nothing is written to disk.
				using (MemoryStream buffered = new MemoryStream())
				{
					byte[] hash = ComputeHashAndCopy(entryStream, buffered, algorithm);
					RecordHash(innerPath, innerPath, hash);
					buffered.Position = 0;
					HashCompressedContents(innerPath, buffered, algorithm);
				}
			}
			else
			{
				byte[] hash = algorithm.ComputeHash(entryStream);
				RecordHash(innerPath, innerPath, hash);
			}
		}

		/// <summary>
		///   Computes a hash of <paramref name="source"/> while copying the same bytes to <paramref name="destination"/>.
		/// </summary>
		/// <param name="source">Stream to read.</param>
		/// <param name="destination">Stream to receive a copy of the bytes.</param>
		/// <param name="algorithm">Hash algorithm instance used by the current thread.</param>
		/// <returns>Hash of the copied bytes.</returns>
		private byte[] ComputeHashAndCopy(Stream source, Stream destination, HashAlgorithm algorithm)
		{
			algorithm.Initialize();
			byte[] buffer = new byte[81920];
			int read;
			while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
			{
				algorithm.TransformBlock(buffer, 0, read, null, 0);
				destination.Write(buffer, 0, read);
			}
			algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
			return algorithm.Hash;
		}

		/// <summary>
		///   Builds a virtual path for a file contained in an archive.
		/// </summary>
		/// <param name="containerPath">Path of the containing archive.</param>
		/// <param name="entryName">Name of the entry inside the archive.</param>
		/// <returns>Path in the form container!entry.</returns>
		private static string GetInnerPath(string containerPath, string entryName)
		{
			return $"{containerPath}!{entryName.Replace('\\', '/').TrimStart('/')}";
		}

		/// <summary>
		///   True if the path refers to a zip or gzip file based on its extension.
		/// </summary>
		private static bool IsCompressedFile(string path)
		{
			return IsZipFile(path) || IsGzFile(path);
		}

		/// <summary>
		///   True if the path has a .zip extension.
		/// </summary>
		private static bool IsZipFile(string path)
		{
			return HasExtension(path, ".zip");
		}

		/// <summary>
		///   True if the path has a .gz, .gzip, or .tgz extension.
		/// </summary>
		private static bool IsGzFile(string path)
		{
			return HasExtension(path, ".gz") || HasExtension(path, ".gzip") || HasExtension(path, ".tgz");
		}

		/// <summary>
		///   Compares the extension of <paramref name="path"/> to <paramref name="extension"/>, ignoring path separators inside archives.
		/// </summary>
		private static bool HasExtension(string path, string extension)
		{
			string name = path;
			int bang = name.LastIndexOf('!');
			if (bang >= 0)
				name = name.Substring(bang + 1);
			int slash = name.LastIndexOf('/');
			int backslash = name.LastIndexOf('\\');
			int sep = Math.Max(slash, backslash);
			if (sep >= 0)
				name = name.Substring(sep + 1);
			return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		///   True if the zip entry represents a directory rather than a file.
		/// </summary>
		private static bool IsZipDirectory(ZipArchiveEntry entry)
		{
			if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
				return true;
			return string.IsNullOrEmpty(Path.GetFileName(entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
		}

		/// <summary>
		///   Default inner file name for a gzip archive when the gzip header does not include one.
		/// </summary>
		private static string GetDefaultGzInnerName(string containerPath)
		{
			string name = Path.GetFileName(containerPath);
			int bang = containerPath.LastIndexOf('!');
			if (bang >= 0)
				name = containerPath.Substring(bang + 1);

			if (name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
				return name.Substring(0, name.Length - 4) + ".tar";
			if (name.EndsWith(".gzip", StringComparison.OrdinalIgnoreCase))
				return name.Substring(0, name.Length - 5);
			if (name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
				return name.Substring(0, name.Length - 3);
			return name;
		}

		/// <summary>
		///   Reads the original file name from a gzip header when present.
		/// </summary>
		/// <param name="stream">Seekable stream positioned at the start of the gzip file.</param>
		/// <returns>Original file name, or null if it is not present or could not be read.</returns>
		private static string GetGzipOriginalFileName(Stream stream)
		{
			if (!stream.CanSeek)
				return null;

			long start = stream.Position;
			try
			{
				int id1 = stream.ReadByte();
				int id2 = stream.ReadByte();
				int cm = stream.ReadByte();
				int flg = stream.ReadByte();
				if (id1 != 0x1F || id2 != 0x8B || cm != 8 || flg < 0)
					return null;

				stream.Seek(6, SeekOrigin.Current); // MTIME, XFL, OS

				if ((flg & 0x04) != 0) // FEXTRA
				{
					int xlen = stream.ReadByte() | (stream.ReadByte() << 8);
					if (xlen < 0)
						return null;
					stream.Seek(xlen, SeekOrigin.Current);
				}

				if ((flg & 0x08) == 0) // FNAME not set
					return null;

				var nameBytes = new List<byte>();
				int b;
				while ((b = stream.ReadByte()) > 0)
					nameBytes.Add((byte)b);
				if (b < 0)
					return null;
				return Encoding.Latin1.GetString(nameBytes.ToArray());
			}
			catch (IOException)
			{
				return null;
			}
			finally
			{
				stream.Position = start;
			}
		}

		/// <summary>
		///   Gets a string representation of the hash value.
		/// </summary>
		/// <param name="hash">Hash value to turn into a string.</param>
		/// <returns>String representation of the hash value.</returns>
		private string GetHashString(byte[] hash)
		{
			if (_dispInHex)
			{
				StringBuilder sb = new StringBuilder(hash.Length * 2);
				foreach (byte b in hash)
					sb.Append(b.ToString("x2")); // Convert each byte to a two-digit hexadecimal string
				return sb.ToString();
			}
			return Convert.ToBase64String(hash);
		}

		#endregion
	}
}
