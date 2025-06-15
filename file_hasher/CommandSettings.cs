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
using System.Security.Cryptography;

namespace file_hasher
{
	[Usage("Hashes all the files found in a folder (and subfolders) and looks for duplicates.", "file_hashers [-a=<algorithm>] [-e] [-s] -i=<input folder 1>,<input folder 2> [-d=<duplicate output file>] [-o=<output file>]")]
	internal class CommandSettings
	{
		#region Arguments

		[Argument('a', "Algorithm to use for hashing the files. Options are SHA256 or MD5. Defaults to SHA256 if not provided.", Word = "algorithm")]
		public string HashAlgorithm { get; set; } = "SHA256";

		[Argument('d', "Tracks duplicate files and outputs them to the specified file.", Word = "dup")]
		public string DuplicateFilePath { get; set; } = null;

		[Argument('i', "Folders containing files and sub-folders of files to be hashed.", Word = "input")]
		public string[] InputFolders { get; set; } = null;

		[Argument('o', "Creates a csv file containing all the files found and their hash.", Word = "output")]
		public string OutputPath { get; set; } = null;

		[Argument('e', "Displays all files and folders that caused an error when accessed. This is most likely due to inaccessibility.", Word = "error")]
		public bool ErrorOnAccess { get; set; } = false;

		[Argument('s', "Processes symbolic links as if they were normal files. If not specified, then symbolic links to files and folders are ignored.", Word = "symlinks")]
		public bool IncludeLinks { get; set; } = false;

		[Argument('t', "Determines the format of the hash. Options are 'hex' or 'base64'. Defaults to 'hex' if not provided.", Word = "type")]
		public string HashFormatString { get; set; } = "hex";

		#endregion

		#region Enums

		public enum  OutputFormat
		{
			/// <summary>
			///   Output is in hexadecimal format.
			/// </summary>
			Hex,

			/// <summary>
			///   Output is in Base64 format.
			/// </summary>
			Base64,
		}

		#endregion

		#region Properties

		public HashAlgorithm Algorithm { get; private set; }

		public OutputFormat HashFormat { get; private set; }

		#endregion

		public string ValidateSettings()
		{
			if(InputFolders == null)
				return $"The input folder was not specified.";

			foreach (string inputFolder in InputFolders)
			{
				if (!Directory.Exists(inputFolder))
					return $"The input folder could not be located.";
			}

			if (HashAlgorithm == "SHA256")
				Algorithm = SHA256.Create();
			else if (HashAlgorithm == "MD5")
				Algorithm = MD5.Create();
			else
				return $"The specified hash algorithm '{HashAlgorithm}' is not supported. Use 'SHA256' or 'MD5'.";

			if (string.Compare(HashFormatString, "hex", true) == 0)
				HashFormat = OutputFormat.Hex;
			else if (string.Compare(HashFormatString, "base64", true) == 0)
				HashFormat = OutputFormat.Base64;
			else
				return $"The specified output type '{HashFormatString}' is not supported. Use 'hex' or 'base64'.";

			return null;
		}
	}
}
