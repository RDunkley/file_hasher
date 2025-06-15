//********************************************************************************************************************************
// Filename:    ConsoleArgs.cs
// Owner:       Richard Dunkley
// Description: This class uses reflection to analyze a class for attributes that it uses to pull arguments from the command line
//              and place the arguments in the properties of the class.
//********************************************************************************************************************************
// Copyright © Richard Dunkley 2025
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the
// License. You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0  Unless required by applicable
// law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and
// limitations under the License.
//********************************************************************************************************************************
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace System
{
	/// <summary>
	///   Attribute to apply to classes.
	/// </summary>
	/// <remarks>
	///   This attribute determines that the corresponding class will be used for command line arguments. It also outlines the
	///   usage text to be added to the help text.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Class)]
	public class Usage : Attribute
	{
		#region Fields

		/// <summary>
		///   Stores the description associated with the settings class.
		/// </summary>
		private readonly string mDescription;

		/// <summary>
		///   Stores the usage statement associated with the settings class.
		/// </summary>
		private readonly string mUsageStatement;

		#endregion Fields

		#region Methods

		/// <summary>
		///   Instantiates a new <see cref="Usage"/> <see cref="Attribute"/> object.
		/// </summary>
		/// <param name="description">Description to include at the start of the help text describing the command line program.</param>
		/// <param name="usage">Directs the user on how to use the command line program.</param>
		/// <exception cref="ArgumentNullException"><paramref name="description"/> or <paramref name="usage"/> is a null reference.</exception>
		public Usage(string description, string usage)
		{
			mDescription = description ?? throw new ArgumentNullException(nameof(description));
			mUsageStatement = usage ?? throw new ArgumentNullException(nameof(usage));
		}

		/// <summary>
		///   Gets the description pulled from the class attribute.
		/// </summary>
		/// <returns>String containing the description.</returns>
		public string GetDescription()
		{
			return mDescription;
		}

		/// <summary>
		///   Gets the usage statement pulled from the class attribute.
		/// </summary>
		/// <returns>String containing the usage statement.</returns>
		public string GetUsageStatement()
		{
			return mUsageStatement;
		}

		#endregion Methods
	}

	/// <summary>
	///   Attribute to apply to the individual properties of a class to determine which should be used for argument input.
	/// </summary>
	/// <remarks>
	///   This attribute flags the property so that <see cref="ConsoleArgs"/> class knows to use it to parse arguments into. The
	///   accepted property types are: Boolean, Byte, Char, DateTime, System.DBNull, Decimal, Double, System.Enum, Int16, Int32,
	///   Int64, SByte, Single, String, UInt16, UInt32, and UInt64. It also supports arrays of the previous types.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Property)]
	public class Argument : Attribute
	{
		#region Fields

		/// <summary>
		///   Determines if the argument is required or not.
		/// </summary>
		/// <remarks>This value is optional and defaults to false.</remarks>
		public bool Required;

		/// <summary>
		///   Single character keyword used to identify the corresponding argument.
		/// </summary>
		/// <remarks>
		///   Each input argument has a shorthand keyword and an optional longhand keyword to identify it. This is the shorthand version
		///   This parameter is required in the attribute.
		/// </remarks>
		private readonly char SingleChar;

		/// <summary>
		///   Contains a description of the argument associated with the property. 
		/// </summary>
		/// <remarks>
		///   This description is used in the help text to identify to the user what the corresponding argument does. This
		///   parameter is required in the attribute.
		/// </remarks>
		private readonly string Description;

		/// <summary>
		///   Contains the word used for the longhand version of the input keyword.
		/// </summary>
		/// <remarks>
		///   Each input argument can optionally have a longhand version that is composed of a word. This parameter is not
		///   required, but is available if something beyond the single character is desired.
		/// </remarks>
		public string Word;

		#endregion Fields

		#region Methods

		/// <summary>
		///   Instatiates a new <see cref="Argument"/> attribute.
		/// </summary>
		/// <param name="singleChar">
		///   Single character keyword used to identify the argument. Must be a lower case or upper case letter. Character is case
		///   sensitive.
		/// </param>
		/// <param name="description">
		///   Description of the argument associated with the property. Used for the help text. Can be null, but the corresponding
		///   help text will contain an empty string.
		/// </param>
		public Argument(char singleChar, string description)
		{
			if (!Char.IsLetter(singleChar))
				throw new ArgumentException(string.Format("The singleChar specified ({0}) is not a letter.", singleChar));

			SingleChar = singleChar;
			Description = description;
			Required = false;
		}

		/// <summary>
		///   Gets the single character keyword associated with the corresponding argument.
		/// </summary>
		/// <returns>Single character letter keyword.</returns>
		public char GetSingleChar()
		{
			return SingleChar;
		}

		/// <summary>
		///   Gets the description of the argument.
		/// </summary>
		/// <returns>String containing the description.</returns>
		public string GetDescription()
		{
			return Description;
		}

		#endregion Methods
	}

	/// <summary>
	///   Static class to access native methods for retrieving the command line arguments.
	/// </summary>
	public static class Kernel32
	{
		/// <summary>
		///   Pinvokes into kernel32.dll to retrieve the command line arguments for the current process when running in Windows.
		/// </summary>
		/// <returns>Pointer to command line string.</returns>
		[DllImport("kernel32.dll", CharSet = CharSet.Auto, EntryPoint = "GetCommandLine")]
		public static extern IntPtr Win_GetCommandLine();
	}

	/// <summary>
	/// Static class used to parse the command line attributes and populate a class object.
	/// </summary>
	public static class ConsoleArgs<T> where T : class
	{
		#region Classes

		/// <summary>
		///   Parses the type and generates lookup tables for the properties, based on their arguments.
		/// </summary>
		private class PropertyLookup
		{
			#region Enumerations

			/// <summary>
			///   Enumerates the various property types supported by the <see cref="ConsoleArgs{T}"/> class.
			/// </summary>
			public enum PropType
			{
				/// <summary>
				///   Represents no property type or property type that was not recognized.
				/// </summary>
				None,

				/// <summary>
				///   Represents a single flag type or boolean property.
				/// </summary>
				Flag,

				/// <summary>
				///   Represents an array of value types.
				/// </summary>
				Array,

				/// <summary>
				///   Represents a single value type.
				/// </summary>
				Single,
			}

			#endregion Enumerations

			#region Fields

			/// <summary>
			///   Lookup table containing the attribute key and associated property information for array properties.
			/// </summary>
			private Dictionary<string, PropertyInfo> mArrayProperties = new Dictionary<string, PropertyInfo>();

			/// <summary>
			///   Lookup table containing the attribute key and associated property information for flag properties (boolean).
			/// </summary>
			private Dictionary<string, PropertyInfo> mFlagProperties = new Dictionary<string, PropertyInfo>();

			/// <summary>
			///   Lookup table containing the attribute key and associated property information for value properties.
			/// </summary>
			private Dictionary<string, PropertyInfo> mValueProperties = new Dictionary<string, PropertyInfo>();

			#endregion Fields

			#region Properties

			/// <summary>
			///   Gets an array of all the <see cref="PropertyInfo"/> objects that are tied to an argument.
			/// </summary>
			public PropertyInfo[] PropertiesWithArguments { get; private set; }

			/// <summary>
			///   Gets the usage string associated with the class.
			/// </summary>
			public string UsageString { get; private set; }

			/// <summary>
			///   Gets the description string associated with the class.
			/// </summary>
			public string Description { get; private set; }

			#endregion Properties

			#region Methods

			/// <summary>
			///   Instantiates a new <see cref="PropertyLookup"/> type based on the specified <see cref="Type"/>.
			/// </summary>
			/// <param name="type"><see cref="Type"/> to parse for properties containing <see cref="Argument"/> attributes.</param>
			/// <exception cref="ArgumentException">
			///   <paramref name="type"/> is not a class type, does not have a <see cref="Usage"/> attribute or an invalid one,
			///   or has a property with an invalid 'Argument' attribute.
			/// </exception>
			/// <exception cref="ArgumentNullException"><paramref name="type"/> is a null reference.</exception>
			public PropertyLookup(Type type)
			{
				if (type == null)
					throw new ArgumentNullException("type");
				if (!type.IsClass)
					throw new ArgumentException("The type specified is not a class type.", "type");

				UsageString = GetUsageStatement(type);
				Description = GetDescription(type);

				// Parse the type information.
				PropertyInfo[] props = type.GetProperties();
				List<PropertyInfo> propList = new List<PropertyInfo>();
				foreach (PropertyInfo prop in props)
				{
					if (prop.CanWrite)
					{
						Attribute propAttr = null;
						try
						{
							propAttr = GetArgumentAttribute(prop);
						}
						catch(ArgumentException e)
						{
							throw new ArgumentException(
								string.Format("The type specified contained a property ({0}) with an invalid 'Argument' attribute ({1}).",
								prop.Name, e.Message), e);
						}

						if (propAttr != null)
						{
							propList.Add(prop);

							Argument arg = (Argument)propAttr;
							if (prop.PropertyType == typeof(bool))
							{
								string[] keys = GetKeys(arg);
								foreach (string key in keys)
									mFlagProperties.Add(key, prop);
							}
							else
							{
								if (prop.PropertyType.IsArray)
								{
									string[] keys = GetKeys(arg);
									foreach (string key in keys)
										mArrayProperties.Add(key, prop);
								}
								else
								{
									string[] keys = GetKeys(arg);
									foreach (string key in keys)
										mValueProperties.Add(key, prop);
								}
							}
						}
					}
				}

				PropertiesWithArguments = propList.ToArray();
			}

			/// <summary>
			///   Generates the help text associated with the property.
			/// </summary>
			/// <param name="prop"><see cref="PropertyInfo"/> object to generate the help text for.</param>
			/// <param name="maxLineWidth">Maximum number of characters allowed in a line.</param>
			/// <returns>Multi-line string containing the help text associated with the property.</returns>
			/// <exception cref="ArgumentException"><paramref name="prop"/> does not match a property in <see cref="PropertiesWithArguments"/>.</exception>
			/// <exception cref="ArgumentNullException"><paramref name="prop"/> is a null reference.</exception>
			public string GeneratePropertyHelpText(PropertyInfo prop, int maxLineWidth)
			{
				if (prop == null)
					throw new ArgumentNullException("prop");

				List<PropertyInfo> propList = new List<PropertyInfo>(PropertiesWithArguments);
				if (!propList.Contains(prop))
					throw new ArgumentException("The PropertyInfo object provided does not match a property that contains an 'Argument' attribute.", "prop");

				if (mFlagProperties.ContainsValue(prop))
					return GenerateFlagPropertyHelpText(prop, maxLineWidth);
				if (mArrayProperties.ContainsValue(prop))
					return GenerateArrayPropertyHelpText(prop, maxLineWidth);
				return GenerateValuePropertyHelpText(prop, maxLineWidth);
			}

			/// <summary>
			///  Generates the help text for an array property object.
			/// </summary>
			/// <param name="prop"><see cref="PropertyInfo"/> object containing the array property information.</param>
			/// <param name="maxLineWidth">Maximum number of characters allowed in a line.</param>
			/// <exception cref="ArgumentException">
			///   <paramref name="prop"/> does not contain an <see cref="Argument"/> <see cref="Attribute"/>.
			/// </exception>
			private string GenerateArrayPropertyHelpText(PropertyInfo prop, int maxLineWidth)
			{
				Argument arg = (Argument)GetArgumentAttribute(prop);

				StringBuilder sb = new StringBuilder();
				sb.AppendLine("    " + GenerateKeyString(arg) + "=" + prop.Name + ",...");
				GenerateArgumentDescription(sb, arg, maxLineWidth);
				return sb.ToString();
			}

			/// <summary>
			///   Generates the argument descrption lines.
			/// </summary>
			/// <param name="sb"><see cref="StringBuilder"/> object to add the lines to.</param>
			/// <param name="arg"><see cref="Argument"/> object to pull the description from.</param>
			/// <param name="maxLineWidth">Maximum number of characters allowed in a line.</param>
			private void GenerateArgumentDescription(StringBuilder sb, Argument arg, int maxLineWidth)
			{
				if (!arg.Required)
				{
					sb.Append("        [Optional] - ");
					WrapLines(sb, arg.GetDescription(), maxLineWidth, 21, 8);
				}
				else
				{
					sb.Append("        ");
					WrapLines(sb, arg.GetDescription(), maxLineWidth, 8, 8);
				}
			}

			/// <summary>
			///   Generates the help text for a flag property object.
			/// </summary>
			/// <param name="prop"><see cref="PropertyInfo"/> object containing the boolean property information.</param>
			/// <param name="maxLineWidth">Maximum number of characters allowed in a line.</param>
			/// <exception cref="ArgumentException">
			///   <paramref name="prop"/> does not contain an <see cref="Argument"/> <see cref="Attribute"/>.
			/// </exception>
			private string GenerateFlagPropertyHelpText(PropertyInfo prop, int maxLineWidth)
			{
				Argument arg = (Argument)GetArgumentAttribute(prop);

				StringBuilder sb = new StringBuilder();
				sb.AppendLine("    " + GenerateKeyString(arg));
				GenerateArgumentDescription(sb, arg, maxLineWidth);
				return sb.ToString();
			}

			/// <summary>
			///   Generates the help text for a property object.
			/// </summary>
			/// <param name="prop"><see cref="PropertyInfo"/> object containing the property information.</param>
			/// <param name="maxLineWidth">Maximum number of characters allowed in a line.</param>
			/// <exception cref="ArgumentException">
			///   <paramref name="prop"/> does not contain an <see cref="Argument"/> <see cref="Attribute"/>.
			/// </exception>
			private string GenerateValuePropertyHelpText(PropertyInfo prop, int maxLineWidth)
			{
				Argument arg = (Argument)GetArgumentAttribute(prop);

				StringBuilder sb = new StringBuilder();
				sb.AppendLine("    " + GenerateKeyString(arg) + "=" + prop.Name);
				GenerateArgumentDescription(sb, arg, maxLineWidth);
				return sb.ToString();
			}

			/// <summary>
			///   Generates a string of all the possible keys associated with the attribute.
			/// </summary>
			/// <param name="arg"><see cref="Argument"/> object contiaining information about the argument.</param>
			/// <returns>String containing a list of all the possible keys associated with <paramref name="arg"/>.</returns>
			private string GenerateKeyString(Argument arg)
			{
				StringBuilder sb = new StringBuilder();
				string[] keys = GetKeys(arg);
				bool first = true;
				foreach (string key in keys)
				{
					if (first)
					{
						sb.Append(key);
						first = false;
					}
					else
					{
						sb.Append(",");
						sb.Append(key);
					}
				}
				return sb.ToString();
			}

			/// <summary>
			///   Gets the <see cref="Argument"/> <see cref="Attribute"/> from the provided <see cref="PropertyInfo"/>.
			/// </summary>
			/// <param name="source"><see cref="PropertyInfo"/> object pulled from a property in a settings class.</param>
			/// <returns>
			///   <see cref="Argument"/> <see cref="Attribute"/> associated with the property or null if no argument attribute was applied
			///   to the property.
			/// </returns>
			/// <exception cref="ArgumentException">More than one 'Argument' attribute was applied to the property, or the attribute type couldn't be loaded.</exception>
			private Attribute GetArgumentAttribute(PropertyInfo source)
			{
				Attribute attr;
				try
				{
					attr = Attribute.GetCustomAttribute(source, typeof(Argument));
				}
				catch (AmbiguousMatchException e)
				{
					throw new ArgumentException("More than one 'Argument' attribute is tied to the property.", e);
				}
				catch (TypeLoadException e)
				{
					throw new ArgumentException("An error occurred while loading the attribute type for the 'Argument' attribute tied to the property.", e);
				}
				return attr;
			}

			/// <summary>
			///   Gets the various keys associated with an argument.
			/// </summary>
			/// <param name="arg"><see cref="Argument"/> to generate the various possible keys from.</param>
			/// <returns>Array of keys that are associated with the <paramref name="arg"/>.</returns>
			private static string[] GetKeys(Argument arg)
			{
				List<string> list = new List<string>(4)
				{
					arg.GetSingleChar().ToString()
				};
				if (!string.IsNullOrEmpty(arg.Word))
					list.Add(arg.Word);
				return list.ToArray();
			}

			/// <summary>
			///   Gets the <see cref="PropertyInfo"/> object associated with the specified attribute key.
			/// </summary>
			/// <param name="key">Attribute key specified by a <see cref="Argument"/> attribute in a property in the type passed into this class's constructor.</param>
			/// <returns><see cref="PropertyInfo"/> associated with the key or null if no <see cref="PropertyInfo"/> was found.</returns>
			/// <exception cref="ArgumentNullException"><paramref name="key"/> is a null reference.</exception>
			public PropertyInfo GetPropertyInfo(string key)
			{
				if (key == null)
					throw new ArgumentNullException("key");

				if (mFlagProperties.ContainsKey(key))
					return mFlagProperties[key];
				if (mArrayProperties.ContainsKey(key))
					return mArrayProperties[key];
				if (mValueProperties.ContainsKey(key))
					return mValueProperties[key];
				return null;
			}

			/// <summary>
			///   Gets the <see cref="PropType"/> associated with the specified attribute key.
			/// </summary>
			/// <param name="key">Attribute key specified by a <see cref="Argument"/> attribute in a property in the type passed into this class's constructor.</param>
			/// <returns><see cref="PropType"/> associated with the key or <see cref="PropType"/>.<see cref="PropType.None"/> if it was not found.</returns>
			/// <exception cref="ArgumentNullException"><paramref name="key"/> is a null reference.</exception>
			public PropType GetPropertyType(string key)
			{
				if (key == null)
					throw new ArgumentNullException("key");

				if (mFlagProperties.ContainsKey(key))
					return PropType.Flag;
				if (mArrayProperties.ContainsKey(key))
					return PropType.Array;
				if (mValueProperties.ContainsKey(key))
					return PropType.Single;
				return PropType.None;
			}

			/// <summary>
			///   Gets the <see cref="Usage"/> <see cref="Attribute"/>'s usage statement from the provided class type.
			/// </summary>
			/// <param name="source"><see cref="Type"/> of the settings class.</param>
			/// <returns>Usage <see cref="Attribute"/> found on the class type.</returns>
			/// <exception cref="ArgumentException">
			///   There was more than one usage attribute applied to the class, the class type does not contain a usage attribute,
			///   or the usage attribute type could not be loaded.
			/// </exception>
			private string GetUsageStatement(Type source)
			{
				Attribute attr;
				try
				{
					attr = Attribute.GetCustomAttribute(source, typeof(Usage));
				}
				catch (AmbiguousMatchException e)
				{
					throw new ArgumentException("More than one 'Usage' attribute is tied to the type.", e);
				}
				catch (TypeLoadException e)
				{
					throw new ArgumentException("An error occurred while loading the attribute type for the 'Usage' attribute tied to the type.", e);
				}

				if (attr == null)
					throw new ArgumentException("The class does not contain a 'Usage' attribute.");

				Usage use = (Usage)attr;
				return use.GetUsageStatement();
			}

			/// <summary>
			///   Gets the <see cref="Usage"/> <see cref="Attribute"/>'s description from the provided class type.
			/// </summary>
			/// <param name="source"><see cref="Type"/> of the settings class.</param>
			/// <returns>Usage <see cref="Attribute"/> found on the class type.</returns>
			/// <exception cref="ArgumentException">
			///   There was more than one usage attribute applied to the class, the class type does not contain a usage attribute,
			///   or the usage attribute type could not be loaded.
			/// </exception>
			private string GetDescription(Type source)
			{
				Attribute attr;
				try
				{
					attr = Attribute.GetCustomAttribute(source, typeof(Usage));
				}
				catch (AmbiguousMatchException e)
				{
					throw new ArgumentException("More than one 'Usage' attribute is tied to the type.", e);
				}
				catch (TypeLoadException e)
				{
					throw new ArgumentException("An error occurred while loading the attribute type for the 'Usage' attribute tied to the type.", e);
				}

				if (attr == null)
					throw new ArgumentException("The class does not contain a 'Usage' attribute.");

				Usage use = (Usage)attr;
				return use.GetDescription();
			}

			#endregion Methods
		}

		/// <summary>
		///   Parse the command line and generates lookup tables based on the arguments found.
		/// </summary>
		private class ConsoleArgInfo
		{
			#region Fields

			/// <summary>
			///   Stores the command line string passed into the constructor.
			/// </summary>
			private readonly string mCommandLine;

			/// <summary>
			///   Lookup table of the command line tags and values.
			/// </summary>
			private Dictionary<string, string[]> mArgs;

			/// <summary>
			///   Lookup table of the command line tags and their corresponding index in the command line.
			/// </summary>
			private Dictionary<string, int> mArgsIndex;

			/// <summary>
			///   <see cref="PropertyInfo"/> containing the property and argument information on the type.
			/// </summary>
			private PropertyLookup mProps;

			/// <summary>
			///   Lookup table of the <see cref="PropertyInfo"/> object associated with each argument found.
			/// </summary>
			private Dictionary<string, PropertyInfo> mTagProperties = new Dictionary<string, PropertyInfo>();

			/// <summary>
			///   Lookkup table of the <see cref="PropertyLookup.PropType"/> for the property associated with each argument found.
			/// </summary>
			private Dictionary<string, PropertyLookup.PropType> mTagType = new Dictionary<string, PropertyLookup.PropType>();

			#endregion Fields

			#region Methods

			/// <summary>
			///   Instantiates a new <see cref="ConsoleArgInfo"/> object using the provided command line and <see cref="PropertyLookup"/> object.
			/// </summary>
			/// <param name="commandLine">Command line to be parsed.</param>
			/// <param name="props"><see cref="PropertyLookup"/> object containing the argument and property information for the destination class.</param>
			/// <param name="throwErrorOnNotFound">True if the constructor should throw an exception if an argument couldn't be located in <paramref name="props"/>.</param>
			/// <exception cref="InvalidOperationException">
			///   The found argument did not match the corresponding property type or the argument had no corresponding property type and <paramref name="throwErrorOnNotFound"/> was true.
			/// </exception>
			public ConsoleArgInfo(string commandLine, PropertyLookup props, bool throwErrorOnNotFound = true)
			{
				mProps = props ?? throw new ArgumentNullException("props");
				if (string.IsNullOrEmpty(commandLine))
				{
					mCommandLine = string.Empty;
					return;
				}

				mCommandLine = commandLine;
				TokenizeCommandLine();
				//SortTokens();
				FindProperties(throwErrorOnNotFound);
			}

			/// <summary>
			///   Converts a binary string to a binary value.
			/// </summary>
			/// <param name="value">Binary string to be converted. Must not include trailing 'b'.</param>
			/// <param name="numBits">Number of bits in the value to be converted.</param>
			/// <returns>Unsigned long containing the converted value.</returns>
			/// <exception cref="OverflowException">The binary value provided is larger than the number of bits allowed.</exception>
			/// <exception cref="FormatException"><paramref name="value"/> contains a character that is not a 1 or 0.</exception>
			/// <exception cref="ArgumentException"><paramref name="numBits"/> is less than 1 or greater than 64.</exception>
			private ulong ConvertBinaryNumber(string value, int numBits)
            {
				if (numBits < 1 || numBits > 64)
					throw new ArgumentException($"The number of bits provided ({numBits}) is less than 1 or greater than 64.");
				if (value.Length > numBits)
					throw new OverflowException($"The binary value provided ({value}) is larger than the number of bits allowed for this type ({numBits}).");

				ulong val = 0;
				for (int i = 0; i < value.Length; i++)
                {
					if (value[i] == '1')
					{
						val |= 1;
					}
					else if (value[i] == '0')
					{
						// do nothing;
					}
					else
					{
						throw new FormatException($"The binary value provided ({value}) contains a character ({value[i]}) that is not a 1 or 0.");
					}

					if(i != value.Length - 1)
						val <<= 1;
                }

				return val;
            }

			/// <summary>
			///   Converts a string value to the specified type.
			/// </summary>
			/// <param name="value">String representation of the value to be converted.</param>
			/// <param name="type">Type to convert the value to.</param>
			/// <returns>Object of the converted value.</returns>
			private object ConvertValue(string value, Type type)
			{
				if (type == typeof(sbyte))
				{
					// Attempt to parse the number as just an integer.
					return sbyte.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(byte))
				{
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'h') // Number is a hexadecimal type 1 number (FFh).
						return byte.Parse(value.Substring(0, value.Length - 1).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 2 && value[0] == '0' && char.ToLower(value[1]) == 'x') // Number is specified as a hexadecimal type 2 number (0xFF).
						return byte.Parse(value.Substring(2).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'b') // Number is a binary number (01101b).
						return (byte)ConvertBinaryNumber(value.Substring(0, value.Length - 1).Replace("_", ""), 8);

					// Attempt to parse the number as just an integer.
					return byte.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(short))
				{
					// Attempt to parse the number as just an integer.
					return short.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(ushort))
				{
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'h') // Number is a hexadecimal type 1 number (FFh).
						return ushort.Parse(value.Substring(0, value.Length - 1).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 2 && value[0] == '0' && char.ToLower(value[1]) == 'x') // Number is specified as a hexadecimal type 2 number (0xFF).
						return ushort.Parse(value.Substring(2).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'b') // Number is a binary number (01101b).
						return (ushort)ConvertBinaryNumber(value.Substring(0, value.Length - 1).Replace("_", ""), 16);

					// Attempt to parse the number as just an integer.
					return ushort.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(int))
				{
					// Attempt to parse the number as just an integer.
					return int.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(uint))
				{
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'h') // Number is a hexadecimal type 1 number (FFh).
						return uint.Parse(value.Substring(0, value.Length - 1).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 2 && value[0] == '0' && char.ToLower(value[1]) == 'x') // Number is specified as a hexadecimal type 2 number (0xFF).
						return uint.Parse(value.Substring(2).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'b') // Number is a binary number (01101b).
						return (uint)ConvertBinaryNumber(value.Substring(0, value.Length - 1).Replace("_", ""), 32);

					// Attempt to parse the number as just an integer.
					return uint.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(long))
				{
					// Attempt to parse the number as just an integer.
					return long.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if (type == typeof(ulong))
				{
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'h') // Number is a hexadecimal type 1 number (FFh).
						return ulong.Parse(value.Substring(0, value.Length - 1).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 2 && value[0] == '0' && char.ToLower(value[1]) == 'x') // Number is specified as a hexadecimal type 2 number (0xFF).
						return ulong.Parse(value.Substring(2).Replace("_", ""), NumberStyles.AllowHexSpecifier);
					if (value.Length > 1 && char.ToLower(value[value.Length - 1]) == 'b') // Number is a binary number (01101b).
						return ConvertBinaryNumber(value.Substring(0, value.Length - 1).Replace("_", ""), 64);

					// Attempt to parse the number as just an integer.
					return ulong.Parse(value.Replace("_", ""), NumberStyles.Integer | NumberStyles.AllowThousands);
				}
				if(type == typeof(TimeSpan))
				{
					return TimeSpan.Parse(value);
				}
				return Convert.ChangeType(value, type);
			}

			/// <summary>
			///   Associates the arguments with their corresponding properties.
			/// </summary>
			/// <param name="throwErrorOnNotFound">True if an error should be thrown if a corresponding property could not be located, false to ignore the argument.</param>
			/// <exception cref="InvalidOperationException">
			///   The found argument did not match the corresponding property type or the argument had no corresponding property type and <paramref name="throwErrorOnNotFound"/> was true.
			/// </exception>
			private void FindProperties(bool throwErrorOnNotFound)
			{
				foreach(string tag in mArgs.Keys)
				{
					int keyIndex = mArgsIndex[tag];
					PropertyLookup.PropType propType = mProps.GetPropertyType(tag);
					if (propType == PropertyLookup.PropType.Single)
					{
						if (mArgs[tag].Length == 0)
						{
							// Single found, but no following value was present.
							string[] lines = GenerateErrorString(mCommandLine, keyIndex);
							throw new InvalidOperationException(string.Format("ERROR: Found a tag ({0}) corresponding to a single value, but no value was found after the tag (Ex: --tag <value> or --tag=<value>).\n{1}\n{2}", keyIndex, lines[0], lines[1]));
						}
						else if(mArgs[tag].Length != 1)
						{
							// Single found, but multiple values are present.
							string[] lines = GenerateErrorString(mCommandLine, keyIndex);
							throw new InvalidOperationException(string.Format("ERROR: Found a tag ({0}) corresponding to a single value, but multiple values were found after the tag. Only one is allowed for this type.\n{1}\n{2}", keyIndex, lines[0], lines[1]));
						}
						else
						{
							mTagProperties.Add(tag, mProps.GetPropertyInfo(tag));
							mTagType.Add(tag, PropertyLookup.PropType.Single);
						}
					}
					else if(propType == PropertyLookup.PropType.Flag)
					{
						int count = mArgs[tag].Length;
						if (count > 0)
						{
							// Flag found, but a value was also present.
							string[] lines = GenerateErrorString(mCommandLine, keyIndex);
							throw new InvalidOperationException(string.Format("ERROR: Found a tag ({0}) corresponding to a flag, but one or more values were found after the tag.\n{1}\n{2}", keyIndex, lines[0], lines[1]));
						}
						else
						{
							mTagProperties.Add(tag, mProps.GetPropertyInfo(tag));
							mTagType.Add(tag, PropertyLookup.PropType.Flag);
						}
					}
					else if(propType == PropertyLookup.PropType.Array)
					{
						mTagProperties.Add(tag, mProps.GetPropertyInfo(tag));
						mTagType.Add(tag, PropertyLookup.PropType.Array);
					}
					else
					{
						if(throwErrorOnNotFound)
						{
							string[] lines = GenerateErrorString(mCommandLine, keyIndex);
							throw new InvalidOperationException(string.Format("ERROR: Found a tag ({0}), but it does not appear to be a valid command line argument.\n{1}\n{2}", keyIndex, lines[0], lines[1]));
						}
					}
				}
			}

			/// <summary>
			///   Gets the text inside a quoted region.
			/// </summary>
			/// <param name="commandLine">Command line being parsed.</param>
			/// <param name="index">Index of the start of the quote (including the quoted character).</param>
			/// <param name="quotedText">Return text in the quoted region (not including the quote characters).</param>
			/// <returns>Index in <paramref name="commandLine"/> following the end quote character. Can be equal to <paramref name="commandLine"/>.Length if the quoted region went to the end of the line.</returns>
			private static int GetQuotedText(string commandLine, int index, out string quotedText)
			{
				var quoteChar = commandLine[index++];

				int start = index;
				while (index < commandLine.Length)
				{
					if (commandLine[index] == quoteChar)
					{
						quotedText = commandLine.Substring(start, index - start);
						return index + 1;
					}
					index++;
				}

				// End of command line reached before end quote was found.
				quotedText = null;
				return commandLine.Length;
			}

			/// <summary>
			///   Gets a tag from the command line starting at <paramref name="index"/>.
			/// </summary>
			/// <param name="commandLine">Command line currently being parsed.</param>
			/// <param name="index">Index in the command line of the starting '-' representing a tag.</param>
			/// <param name="tag">Tag value parsed from <paramref name="commandLine"/>. Can be single character or whole word.</param>
			/// <returns>Index to the value starting after the tag. This is after the space or '=' following the tag.</returns>
			/// <remarks>Will search for --tag or -tag values.</remarks>
			/// <exception cref="InvalidOperationException">An invalid tag was found (partial or invalid characters in the tag.</exception>
			private static int GetTag(string commandLine, int index, out string tag)
			{
				if (index == commandLine.Length)
					throw new InvalidOperationException($"A '-' was found at index {index - 1}, but it at the end of the command line without an actual tag.");

				int tagStart = index - 1;
				if (commandLine[index] == '-')
				{
					// Skip the additional '-'.
					index++;
				}

				// Check that first character is a letter.
				if (!char.IsLetter(commandLine[index]))
					throw new InvalidOperationException($"A '-' or '--' was found at index {tagStart}, but the first character of the argument tag ({commandLine[index]}) is not a letter. The first character must be a letter.");

				// Check that additional characters are letters or numbers.
				int start = index;
				index++; // Skip the first letter since it was checked above.
				while (index < commandLine.Length)
				{
					if (!char.IsLetterOrDigit(commandLine[index]))
					{
						if (char.IsWhiteSpace(commandLine[index]) || commandLine[index] == '=')
						{
							// Found a valid tag.
							tag = commandLine.Substring(start, index - start);
							return index + 1;
						}
						throw new InvalidOperationException($"A '-' or '--' was found at index {tagStart}, but the {index - start} character of the argument tag ({commandLine[index]}) is not a letter or digit. The first character must be a letter, but all other characters in the tag must be a letter or number.");
					}
					index++;
				}

				tag = commandLine.Substring(start, index - start);
				return commandLine.Length;
			}

			/// <summary>
			///   Gets the values after a tag.
			/// </summary>
			/// <param name="commandLine">Command line to be parsed.</param>
			/// <param name="index">Index into <paramref name="commandLine"/> to begin looking for the values at.</param>
			/// <param name="values">Array of values to return.</param>
			/// <returns>One more than the final character in the values. This allows the caller to continue to parse the next tag from this point.</returns>
			/// <remarks>This will keep pulling the following characters until the next tag or end of line is found.</remarks>
			/// <exception cref="InvalidOperationException">A quoted region was started (using ',", or `, but the end was never found.</exception>
			private static int GetValues(string commandLine, int index, out string[] values)
			{
				var list = new List<string>();
				int start = index;
				while (index < commandLine.Length)
				{
					if (commandLine[index] == '"' || commandLine[index] == '\'' || commandLine[index] == '`')
					{
						// Quoted value found.
						int end = GetQuotedText(commandLine, index, out string quotedText);
						if (quotedText == null)
							throw new InvalidOperationException($"Unable to find the end of the quote starting with character {commandLine[index]} at index {index}");
						list.Add(quotedText.Trim());
						start = end;
						index = end;
					}
					else if (commandLine[index] == ',')
					{
						// Next value so finish the previous if it wasn't already added (quoted text).
						if (start != index)
						{
							var sub = commandLine.Substring(start, index - start);
							if(!string.IsNullOrWhiteSpace(sub))
								list.Add(sub.Trim());
						}
						index++;
						start = index;
					}
					else if (commandLine[index] == '-')
					{
						// We can back index here because we know that a tag has already been found since we are looking for values.
						if (char.IsWhiteSpace(commandLine[index - 1]))
						{
							// Next tag so we are finished with the values.
							if (start != index)
							{
								var lastString = commandLine.Substring(start, index - start);
								if (!string.IsNullOrWhiteSpace(lastString))
									list.Add(lastString.Trim());
							}
							values = list.ToArray();
							return index;
						}

						// Ignore since it must be a '-' embedded in argument value.
						index++;
					}
					else
					{
						// Character is part of a value so skip it.
						index++;
					}
				}

				// End of command line reached.
				if (start != index)
				{
					var finalString = commandLine.Substring(start, index - start);
					if (!string.IsNullOrWhiteSpace(finalString))
						list.Add(finalString.Trim());
				}
				values = list.ToArray();
				return commandLine.Length;
			}

			/// <summary>
			///   Populates the properties in the <paramref name="settingsObject"/>.
			/// </summary>
			/// <param name="settingsObject">Object to populate the properties of.</param>
			/// <exception cref="InvalidOperationException">An error occurred while attempting to set the property. See Inner Exception.</exception>
			public void PopulateProperties(object settingsObject)
			{
				foreach (string tag in mTagProperties.Keys)
				{
					try
					{
						int keyIndex = mArgsIndex[tag];
						if (mTagType[tag] == PropertyLookup.PropType.Single)
						{
							string value = mArgs[tag][0];
							mTagProperties[tag].SetValue(settingsObject, ConvertValue(value, mTagProperties[tag].PropertyType));
						}
						else if (mTagType[tag] == PropertyLookup.PropType.Array)
						{
							string[] values = mArgs[tag];
							Type elementType = mTagProperties[tag].PropertyType.GetElementType();
							Array arrayList = Array.CreateInstance(elementType, values.Length);
							for (int i = 0; i < values.Length; i++)
								arrayList.SetValue(ConvertValue(values[i], elementType), i);
							mTagProperties[tag].SetValue(settingsObject, arrayList);
						}
						else
						{
							mTagProperties[tag].SetValue(settingsObject, true);
						}
					}
					catch (Exception e)
					{
						if (e is ArgumentException || e is TargetException || e is TargetInvocationException || e is InvalidCastException
							|| e is FormatException || e is OverflowException)
						{
							throw new InvalidOperationException(string.Format("An attempt to set the property associated with an argument ({0}) was unsuccessful ({1}).", tag, e.Message), e);
						}
						throw;
					}
				}
			}

			/// <summary>
			///   Parses the command line to obtain the tags and values.
			/// </summary>
			/// <exception cref="InvalidOperationException">Invalid arguments were found on the command line.</exception>
			private void TokenizeCommandLine()
			{
				var commandLine = mCommandLine;
				mArgs = new Dictionary<string, string[]>();
				mArgsIndex = new Dictionary<string, int>();
				int index = 0;
				bool firstTag = true;
				while(index < commandLine.Length)
				{
					if (commandLine[index] == '-')
					{
						firstTag = false;
						var start = index;
						index = GetTag(commandLine, index + 1, out string tag);
						index = GetValues(commandLine, index, out string[] values);
						if (mArgs.ContainsKey(tag))
							throw new InvalidOperationException($"A second tag was found at index {start}, only one tag is allowed in the command line arguments.");
						mArgs.Add(tag, values);
						mArgsIndex.Add(tag, start);
					}
					else
					{
						if(!firstTag)
							throw new InvalidOperationException($"A character ({commandLine[index]}) was found at index {index}, but it wasn't preceeded by a tag value.");

						// If we haven't seen the first tag then it is probably the executable in the command.
						index++;
					}
				}
			}

			#endregion Methods
		}

		#endregion Classes

		#region Methods

		/// <summary>
		///   Generates an error string from the specified command line and the index where the error occurred.
		/// </summary>
		/// <param name="commandLine">Command line being parsed when the error occurred.</param>
		/// <param name="index">Index in the command line where the error occurred.</param>
		/// <returns>String array containing a string on where the error occurred. This allows a user to quickly locate the error.</returns>
		private static string[] GenerateErrorString(string commandLine, int index)
		{
			int before;
			if (index > 20)
				before = 20;
			else
				before = index;

			int after;
			if (commandLine.Length > index + 20)
				after = 20;
			else
				after = commandLine.Length - index;

			string[] lines = new string[2];
			lines[0] = commandLine.Substring(index - before, after + before);
			lines[1] = string.Format("{0}^{1}", GenerateSpaceString(before), GenerateSpaceString(after));
			return lines;
		}

		/// <summary>
		///   Generates the help text to display to the user for usage explanation.
		/// </summary>
		/// <param name="maxLineWidth">Maximum number of characters per line. This can be pulled using <see cref="Console"/>.<see cref="Console.BufferWidth"/>.</param>
		/// <returns>Multi-line string containing information on how to use the application based on the attributes found in the specified type.</returns>
		/// <remarks>The text is built from the descriptions in the added attributes.</remarks>
		public static string GenerateHelpText(int maxLineWidth)
		{
			// Parse the type information.
			Type settingsType = typeof(T);
			PropertyLookup pl = new PropertyLookup(settingsType);

			StringBuilder helpText = new StringBuilder();
			helpText.AppendLine("NAME");
			helpText.AppendLine("    " + Assembly.GetExecutingAssembly().GetName().Name);
			helpText.AppendLine();

			helpText.AppendLine("SYNOPSIS");
			helpText.Append("    ");
			WrapLines(helpText, pl.UsageString, maxLineWidth, 4, 4);
			helpText.AppendLine();

			helpText.AppendLine("DESCRIPTION");
			helpText.Append("    ");
			WrapLines(helpText, pl.Description, maxLineWidth, 4, 4);
			helpText.AppendLine();

			foreach (PropertyInfo prop in pl.PropertiesWithArguments)
				helpText.Append(pl.GeneratePropertyHelpText(prop, maxLineWidth));

			return helpText.ToString();
		}

		/// <summary>
		///   Writes the line to a string builder object, but will wrap the text lines if they are too long.
		/// </summary>
		/// <param name="sb"><see cref="StringBuilder"/> object to write the line to.</param>
		/// <param name="line">Line to be written.</param>
		/// <param name="maxLineWidth">Maximum number of characters per line.</param>
		/// <param name="currentPos">Current position to begin writing to. This provides an offset if needed to where the line will begin to be written.</param>
		/// <param name="newLineIndent">If the text is wrapped to a new line, this specifies how many spaces are placed to indent the new line.</param>
		private static void WrapLines(StringBuilder sb, string line, int maxLineWidth, int currentPos, int newLineIndent)
		{
			int indent = currentPos;
			bool cantfitText = false;
			while (line != null)
			{
				SplitOnEndOfLine(line, indent, maxLineWidth, out string lineText, out string remainingText);
				if (lineText.Length == 0)
				{
					// Can't fit any text on the line.
					if (cantfitText)
					{
						// This is the second time we can't fit text, which means we can't write any of the text so just write the whole text.
						sb.AppendLine(line);
						return;
					}
					else
					{
						// Skip a line and see if we can write it on the new line with the new line indentation.
						cantfitText = true;
						sb.AppendLine();
						indent = newLineIndent;
					}
				}
				else
				{
					// Write the rest of the line's text.
					sb.AppendLine(lineText);
					if (remainingText != null)
					{
						for (int i = 0; i < newLineIndent; i++)
							sb.Append(' ');
						indent = newLineIndent;
					}
				}
				line = remainingText;
			}
		}

		/// <summary>
		///   Splits the comment text on a space between words, or splits a word and hyphens it.
		/// </summary>
		/// <param name="text">Text to evaluate for splitting.</param>
		/// <param name="lineOffset">Offset in the line where the text will start.</param>
		/// <param name="lineText">Text to be written to the line. Can be empty if there is no room to write any text.</param>
		/// <param name="remainingText">Text remaining that could not fit on the line. Can be null if no text is remaining.</param>
		private static void SplitOnEndOfLine(string text, int lineOffset, int numCharsPerLine, out string lineText, out string remainingText)
		{
			// remove any leading or trailing whitespace.
			text = text.Trim();

			int remainingSpace = numCharsPerLine - lineOffset - 1;
			if (text.Length <= remainingSpace)
			{
				// The text will fit in the space.
				lineText = text;
				remainingText = null;
				return;
			}

			if (remainingSpace < 1)
			{
				// Not enough space to add any text.
				lineText = string.Empty;
				remainingText = text;
				return;
			}

			if (remainingSpace == 1)
			{
				// Might not be enough space if we need a hyphen.
				if (text[1] == ' ')
				{
					// Add single character to text.
					lineText = text[0].ToString();
					remainingText = text.Substring(2);
				}
				else
				{
					// Not enough space to add one character and hyphen.
					lineText = string.Empty;
					remainingText = text;
				}
			}

			int splitIndex = text.LastIndexOf(' ', remainingSpace - 1, remainingSpace);
			if (splitIndex == -1)
			{
				// Break the file on the end of the text and place a hyphen.
				lineText = string.Format("{0}-", text.Substring(0, remainingSpace - 1)); // Should fill remaining space.
				remainingText = text.Substring(remainingSpace - 1);
			}
			else
			{
				lineText = text.Substring(0, splitIndex);
				remainingText = text.Substring(splitIndex + 1);
			}
			return;
		}

		/// <summary>
		///   Generates a string containing the specified number of spaces.
		/// </summary>
		/// <param name="count">Number of spaces contained in the string.</param>
		/// <returns>An empty string if count is less than one, or a string of spaces with a length equal to <paramref name="count"/>.</returns>
		private static string GenerateSpaceString(int count)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < count; i++)
				sb.Append(" ");
			return sb.ToString();
		}

		/// <summary>
		///   Populates the properties in <paramref name="settingsObject"/> based on values provided in <paramref name="commandLine"/>.
		/// </summary>
		/// <param name="commandLine">Command line string used to pull the settings from.</param>
		/// <param name="settingsObject">Class object containing properties to be populated.</param>
		/// <param name="throwErrorOnNotFound">
		///   True if an exception should be thrown if a command line attribute is found that does not have a corresponding property in
		///   <paramref name="settingsObject"/>, false if the attribute should be ignored.
		/// </param>
		public static void Populate(string commandLine, T settingsObject, bool throwErrorOnNotFound = true)
		{
			if (string.IsNullOrEmpty(commandLine))
				return;

			if (settingsObject == null)
				throw new ArgumentNullException("settingsObject");

			PropertyLookup propTable = new PropertyLookup(typeof(T));
			ConsoleArgInfo argInfo = new ConsoleArgInfo(commandLine, propTable, throwErrorOnNotFound);
			argInfo.PopulateProperties(settingsObject);
		}

		/// <summary>
		///   Gets the full command line used to start the application.
		/// </summary>
		/// <returns>Full command line including the executable.</returns>
		/// <exception cref="PlatformNotSupportedException">The current platform is not supported.</exception>
		/// <remarks>
		///   The .Net API for obtaining the command line (Environment.CommandLine) will sometimes modify or reformat the command line. Removing or changing quotation marks.
		///   This method will return the full unmodified command line obtained from the operating system's corresponding API.
		/// </remarks>
		public static string GetFullCommandLine()
		{
			IntPtr cmdLinePtr;
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				cmdLinePtr = Kernel32.Win_GetCommandLine();
				if (cmdLinePtr == IntPtr.Zero)
					return null;
				return Marshal.PtrToStringAuto(cmdLinePtr);
			}
			else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
			{
				return File.ReadAllText("/proc/self/cmdline").Replace('\0', ' ');
			}
			else
				throw new PlatformNotSupportedException("The current platform is not supported for retrieving the command line.");
		}

		#endregion Methods
	}
}