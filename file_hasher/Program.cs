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
using System.Runtime.InteropServices;

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
			foreach (string folder in settings.InputFolders)
				hasher.Hash(folder, null);

			if (settings.OutputPath != null)
			{
				using (StreamWriter wr = new StreamWriter(settings.OutputPath))
				{
					wr.WriteLine("\"File\",\"Hash\"");
					var fileList = hasher.HashByFile.Keys.ToList();
					fileList.Sort();

					foreach (string file in fileList)
						wr.WriteLine($"\"{file}\",\"{hasher.HashByFile[file]}\"");
				}
			}

			if (settings.DuplicateFilePath != null && hasher.DuplicateFiles.Count > 0)
			{
				using (StreamWriter wr = new StreamWriter(settings.DuplicateFilePath))
				{
					wr.WriteLine("\"File\",\"Duplicate Of\"");
					var fileList = hasher.DuplicateFiles.Keys.ToList();
					fileList.Sort();

					foreach (string file in fileList)
						wr.WriteLine($"\"{file}\",\"{hasher.DuplicateFiles[file]}\"");
				}
			}
		}
	}
}
