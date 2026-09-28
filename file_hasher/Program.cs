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
using System.Text;

namespace file_hasher
{
	class Program
	{
		static void Main(string[] args)
		{
			CommandSettings settings = new CommandSettings();
			var cmdLine = ConsoleArgs<CommandSettings>.GetFullCommandLine();
			//Console.WriteLine($"Command Line: {cmdLine}");

			ConsoleArgs<CommandSettings>.Populate(cmdLine, settings);
			if (settings.Help)
			{
				Console.WriteLine(ConsoleArgs<CommandSettings>.GenerateHelpText(Console.BufferWidth));
				return;
			}

			string error = settings.ValidateSettings();
			if (error != null)
			{
				Console.WriteLine($"Error: {error}");
				ConsoleArgs<CommandSettings>.GenerateHelpText(Console.BufferWidth);
				return;
			}

			FolderHasher hasher = new FolderHasher(settings.Algorithm, settings.HashFormat == CommandSettings.OutputFormat.Hex, settings.DuplicateFilePath != null);
			hasher.DisplayWhileProcessing = settings.OutputPath == null;
			hasher.DisplayErrorOnAccess = settings.ErrorOnAccess;
			hasher.ProcessSymLinks = settings.IncludeLinks;
			hasher.ProcessCompressed = settings.ProcessCompressed;
			hasher.ThreadCount = settings.ThreadCount;

			Dictionary<string, string> inputSuffixes = BuildInputSuffixes(settings.InputFolders);
			bool multipleInputs = settings.InputFolders.Length > 1;

			foreach (string inputFolder in settings.InputFolders)
			{
				hasher.Clear();
				hasher.Hash(inputFolder, null);

				if (settings.OutputPath != null)
				{
					string outputPath = GetPerInputOutputPath(settings.OutputPath, inputFolder, multipleInputs, inputSuffixes);
					WriteHashFile(outputPath, hasher.HashByFile, inputFolder);
				}

				if (settings.DuplicateFilePath != null && hasher.DuplicateFiles != null && hasher.DuplicateFiles.Count > 0)
				{
					string dupPath = GetPerInputOutputPath(settings.DuplicateFilePath, inputFolder, multipleInputs, inputSuffixes);
					WriteDuplicateFile(dupPath, hasher.DuplicateFiles, inputFolder);
				}
			}
		}

		/// <summary>
		///   Writes the hash csv with file paths relative to <paramref name="inputFolder"/>.
		/// </summary>
		private static void WriteHashFile(string outputPath, Dictionary<string, string> hashByFile, string inputFolder)
		{
			var rows = hashByFile.Select(pair => (Path: ToRelativeOutputPath(pair.Key, inputFolder), Hash: pair.Value)).ToList();
			rows.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

			using (StreamWriter wr = new StreamWriter(outputPath))
			{
				wr.WriteLine("\"File\",\"Hash\"");
				foreach (var row in rows)
					wr.WriteLine($"\"{row.Path}\",\"{row.Hash}\"");
			}
		}

		/// <summary>
		///   Writes the duplicate csv with file paths relative to <paramref name="inputFolder"/>.
		/// </summary>
		private static void WriteDuplicateFile(string outputPath, Dictionary<string, string> duplicateFiles, string inputFolder)
		{
			var rows = duplicateFiles.Select(pair => (Path: ToRelativeOutputPath(pair.Key, inputFolder), DuplicateOf: ToRelativeOutputPath(pair.Value, inputFolder))).ToList();
			rows.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

			using (StreamWriter wr = new StreamWriter(outputPath))
			{
				wr.WriteLine("\"File\",\"Duplicate Of\"");
				foreach (var row in rows)
					wr.WriteLine($"\"{row.Path}\",\"{row.DuplicateOf}\"");
			}
		}

		/// <summary>
		///   Converts a hashed file path (including optional archive inner paths) to a path relative to the input folder.
		/// </summary>
		private static string ToRelativeOutputPath(string filePath, string inputFolder)
		{
			string archiveInner = null;
			int bang = filePath.IndexOf('!');
			string fsPath = filePath;
			if (bang >= 0)
			{
				fsPath = filePath.Substring(0, bang);
				archiveInner = filePath.Substring(bang + 1);
			}

			string relative = Path.GetRelativePath(Path.GetFullPath(inputFolder), Path.GetFullPath(fsPath));
			relative = relative.Replace('\\', '/');
			if (archiveInner != null)
				relative = $"{relative}!{archiveInner.Replace('\\', '/')}";
			return relative;
		}

		/// <summary>
		///   Returns the output path for one input folder. A single input uses <paramref name="outputPath"/> as-is;
		///   multiple inputs insert a suffix identifying the folder.
		/// </summary>
		private static string GetPerInputOutputPath(string outputPath, string inputFolder, bool multipleInputs, Dictionary<string, string> inputSuffixes)
		{
			if (!multipleInputs)
				return outputPath;

			string directory = Path.GetDirectoryName(outputPath);
			string name = Path.GetFileNameWithoutExtension(outputPath);
			string extension = Path.GetExtension(outputPath);
			if (string.IsNullOrEmpty(name))
				name = "output";

			string fileName = $"{name}.{inputSuffixes[inputFolder]}{extension}";
			return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
		}

		/// <summary>
		///   Builds a unique filename suffix for each input folder from trailing path segments.
		/// </summary>
		private static Dictionary<string, string> BuildInputSuffixes(string[] inputFolders)
		{
			var parts = new string[inputFolders.Length][];
			for (int i = 0; i < inputFolders.Length; i++)
			{
				string full = Path.GetFullPath(inputFolders[i]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				parts[i] = full.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				if (parts[i].Length == 0)
					parts[i] = new[] { "root" };
			}

			int[] depth = Enumerable.Repeat(1, inputFolders.Length).ToArray();
			var suffixes = new string[inputFolders.Length];
			bool expanded;
			do
			{
				for (int i = 0; i < inputFolders.Length; i++)
				{
					int take = Math.Min(depth[i], parts[i].Length);
					suffixes[i] = SanitizeFileName(string.Join("_", parts[i].Skip(parts[i].Length - take)));
				}

				expanded = false;
				foreach (var group in suffixes.Select((suffix, index) => (suffix, index)).GroupBy(item => item.suffix, StringComparer.OrdinalIgnoreCase))
				{
					if (group.Count() <= 1)
						continue;

					foreach (var item in group)
					{
						if (depth[item.index] < parts[item.index].Length)
						{
							depth[item.index]++;
							expanded = true;
						}
					}
				}
			} while (expanded);

			var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var result = new Dictionary<string, string>();
			for (int i = 0; i < inputFolders.Length; i++)
			{
				string suffix = suffixes[i];
				if (string.IsNullOrEmpty(suffix))
					suffix = "root";

				if (used.TryGetValue(suffix, out int count))
				{
					count++;
					used[suffix] = count;
					suffix = $"{suffix}_{count}";
				}
				else
				{
					used[suffix] = 1;
				}

				result[inputFolders[i]] = suffix;
			}

			return result;
		}

		/// <summary>
		///   Replaces characters that are invalid in a file name so the suffix can be used in an output path.
		/// </summary>
		private static string SanitizeFileName(string name)
		{
			char[] invalid = Path.GetInvalidFileNameChars();
			var sb = new StringBuilder(name.Length);
			foreach (char c in name)
				sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);

			string sanitized = sb.ToString().Trim('_');
			return string.IsNullOrEmpty(sanitized) ? "root" : sanitized;
		}
	}
}
