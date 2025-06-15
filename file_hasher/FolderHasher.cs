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
using System.Security;
using System.Security.Cryptography;
using System.Text;

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
		///   Tracks the folders that have been processed, to avoid reprocessing them.
		/// </summary>
		private List<string> _folders = new List<string>();

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
			if (!Directory.Exists(folder))
				throw new ArgumentException($"The folder specified ({folder}) does not exist.");

			HashFolder(folder, cancel);
		}

		/// <summary>
		///   Clears the hash lookup tables and skipped files.
		/// </summary>
		public void Clear()
		{
			DuplicateFiles?.Clear();
			HashByFile.Clear();
		}

		/// <summary>
		///   Hashes all files in the specified folder and its subfolders, adding them to the hash lookup tables.
		/// </summary>
		/// <param name="folder">Folder to be hashed.</param>
		/// <param name="cancel"><see cref="CancellationToken"/> to cancel the hashing.</param>
		/// <exception cref="OperationCanceledException">Thrown if the operation is cancelled.</exception>
		private void HashFolder(string folder, CancellationToken? cancel)
		{
			if (cancel.HasValue && cancel.Value.IsCancellationRequested)
				throw new OperationCanceledException();

			if(!ProcessSymLinks)
			{
				// Ignore symbolic links to folders.
				var dirInfo = new DirectoryInfo(folder);
				if(dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
					return;
			}
			else
			{
				var dirInfo = new DirectoryInfo(folder);
				if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					folder = Directory.ResolveLinkTarget(folder, true).FullName;
					if(_folders.Contains(folder))
						return; // Already processed this folder, so skip it.
				}
			}

			string[] files;
			try
			{
				files = Directory.GetFiles(folder);
			}
			catch(Exception e) when (e is UnauthorizedAccessException || e is SecurityException)
			{
				if(DisplayErrorOnAccess)
					Console.WriteLine($"{folder} -> {e.Message}");
				return;
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

				if (HashByFile.ContainsKey(srcFile))
					continue; // Already processed this file, so skip it.

				byte[] hash = HashFile(file, srcFile);
				if (hash != null)
				{
					string hashString = GetHashString(hash);
					if (!HashByFile.ContainsKey(srcFile))
						HashByFile.Add(srcFile, hashString);
					if (DisplayWhileProcessing)
					{
						if(srcFile == file)
							Console.WriteLine($"{file} -> {hashString}");
						else
							Console.WriteLine($"{file} ({srcFile}) -> {hashString}");
					}

					if (TrackDuplicates)
					{
						if (_fileByHash.ContainsKey(hashString))
						{
							// This file is a duplicate of another file, so create a duplicate link to the old file.
							//Console.WriteLine($"Duplicate found: {file} ({srcFile}) is a duplicate of {_fileByHash[hashString]}");
							DuplicateFiles.Add(file, _fileByHash[hashString]);
						}
						else
						{
							// This is the first file with this hash.
							_fileByHash.Add(hashString, file);
						}
					}
				}
			}

			_folders.Add(folder); // Mark this folder as processed to avoid reprocessing it.
			foreach (string child in Directory.GetDirectories(folder))
				HashFolder(child, cancel);
		}

		/// <summary>
		///   Hashes a single file and returns its hash value.
		/// </summary>
		/// <param name="srcFile">File to be hashed.</param>
		/// <returns>Hash of the file as a byte array, or null if the file could not be accessed.</returns>
		private byte[] HashFile(string file, string srcFile)
		{
			try
			{
				using (FileStream reader = new FileStream(srcFile, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					reader.Position = 0;
					return _algo.ComputeHash(reader);
				}
			}
			catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
			{
				if (DisplayErrorOnAccess)
				{
					if(srcFile == file)
						Console.WriteLine($"{file} -> {e.Message}");
					else
						Console.WriteLine($"{file} ({srcFile}) -> {e.Message}");
				}
				return null;
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
