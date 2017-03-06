/*
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at:
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

class Program
{
    const string XML_INPUT_FILE_PATH = "./input/ZWave_custom_cmd_classes.xml";
    const string JAVASCRIPT_OUTPUT_FILE_PATH = "./output/zwave_command_classes.js";

    static void Main(string[] args)
    {
        Console.WriteLine("Loading XML input file: " + XML_INPUT_FILE_PATH);

        // create a list which will hold our class name/value enumeration pairs
        SortedDictionary<string, byte> commandClassEnumPairs = new SortedDictionary<string, byte>();
        // create a dictionary which will keep track of the latest version number of each command class enum
        Dictionary<string, byte> commandClassesNewestVersionTracker = new Dictionary<string, byte>();
        // create a sorted dictionary of command class enumerations
        SortedDictionary<string, List<KeyValuePair<string, byte>>> commandClassEnumerations = new SortedDictionary<string, List<KeyValuePair<string, byte>>>();

        FileStream fileStream = null;
        try 
        {
            fileStream = new FileStream(XML_INPUT_FILE_PATH, FileMode.Open);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Could not open file: " + XML_INPUT_FILE_PATH);
            Console.WriteLine("exception: " + ex.Message);
            return;
        }

        try     
        {
            XmlReader reader = XmlReader.Create(fileStream);

            try 
            {                    
                while (reader.ReadToFollowing("cmd_class")) 
                {
                    // make sure we are at depth 1 (i.e. reading the correct "cmd_class" entries)
                    if (reader.Depth != 1)
                    {
                        // invalid depth; skip this entry
                        continue;
                    }

                    // retrieve command class name, key (command class #) and version
                    string commandClassName = reader.GetAttribute("name");
                    string commandClassEnumValueAsString = reader.GetAttribute("key");
                    string commandClassVersionAsString = reader.GetAttribute("version");
                    // validate entries
                    if (commandClassName == null) {
                        // command class name missing; skip this entry
                        continue;
                    }
                    if (commandClassEnumValueAsString == null || commandClassEnumValueAsString.Length < 3 || commandClassEnumValueAsString.Substring(0, 2).ToLowerInvariant() != "0x") 
                    {
                        // enum value (command class number) missing; skip this entry
                        continue;
                    }
                    if (commandClassVersionAsString == null) 
                    {
                        // command class version missing; skip this entry
                        continue;
                    }
                    // try to parse the enumValue/version
                    byte commandClassEnumValue;
                    byte commandClassVersion;
                    if (!byte.TryParse(commandClassEnumValueAsString.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out commandClassEnumValue)) 
                    {
                        // enum value (command class number) invalid; skip this entry
                        continue;
                    }
                    if (!byte.TryParse(commandClassVersionAsString, NumberStyles.Number, CultureInfo.InvariantCulture, out commandClassVersion)) 
                    {
                        // command class version invalid; skip this entry
                        continue;
                    }
                    // shorten the command class name
                    string commandClassShortName = ExtractShortCommandClassNameFromLongCommandClassName(commandClassName);
                    if (commandClassShortName == null) 
                    {
                        // command class name invalid; skip this entry
                        continue;
                    }
                    // convert the command class name to an UpperCamelCase enumeration
                    string commandClassEnumName = ConvertUpperCaseUnderscoreSeparatedToLowerCamelCase(commandClassShortName);
                    if (commandClassEnumName == null) 
                    {
                        // command class name invalid; skip this entry
                        continue;
                    }

                    // get the last version of the command class which was already added to our enumeration
                    // NOTE: we do this immediately before adding the command class and initial version to the list, so that we don't capture the just-added version
                    byte? newestVersion = null;
                    if (commandClassesNewestVersionTracker.ContainsKey(commandClassEnumName))
                    {
                        newestVersion = commandClassesNewestVersionTracker[commandClassEnumName];
                    }
                    //
                    // if our command class has not already been added, add it to our command class enumeration now
                    if (!commandClassEnumPairs.ContainsKey(commandClassEnumName))
                    {
                        commandClassEnumPairs.Add(commandClassEnumName, commandClassEnumValue);
                        commandClassesNewestVersionTracker.Add(commandClassEnumName, commandClassVersion);
                    }
                    
                    // now, create an enumeration for the command from the inner xml
                    string commandClassCommandsEnumName = commandClassEnumName + "Command";
                    List<KeyValuePair<string, byte>> commandEnumPairs = ParseXmlForCommandClassCommands(commandClassShortName, reader.ReadOuterXml());
                    
                    if (newestVersion == null || newestVersion < commandClassVersion)
                    {
                        // remove any old versions
                        if (commandClassEnumerations.ContainsKey(commandClassEnumName))
                        {
                            commandClassEnumerations.Remove(commandClassEnumName);
                        }

                        // add the new enumeration values
                        commandClassEnumerations.Add(commandClassEnumName, commandEnumPairs);
                        commandClassesNewestVersionTracker[commandClassEnumName] = commandClassVersion;
                    }
                }
            }
            finally
            {
                reader.Dispose();
            }

            Console.WriteLine("Done reading input file.");
        }
        finally
        {
            fileStream.Dispose();
        }

        Console.WriteLine("Writing JavaScript output file: " + JAVASCRIPT_OUTPUT_FILE_PATH);

        fileStream = null;
        try 
        {
            fileStream = new FileStream(JAVASCRIPT_OUTPUT_FILE_PATH, FileMode.Create);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Could not open file: " + JAVASCRIPT_OUTPUT_FILE_PATH);
            Console.WriteLine("exception: " + ex.Message);
            return;
        }

        try
        {
            StreamWriter writer = new StreamWriter(fileStream);

            try     
            {
                // build and output the command class enumeration
                writer.WriteLine("/* Z-Wave command classes */");
                writer.WriteLine("let CommandClass = Object.freeze({");
                // add each command class (standard lookup)
                foreach (KeyValuePair<string, byte> enumPair in commandClassEnumPairs) 
                {
                    writer.WriteLine("    " + enumPair.Key + ": 0x" + enumPair.Value.ToString("x2") + ",");
                }
                // add each command class (reverse lookup)
                writer.WriteLine("    properties: {");
                foreach (KeyValuePair<string, byte> enumPair in commandClassEnumPairs) 
                {
                    writer.WriteLine("        0x" + enumPair.Value.ToString("x2") + ": {name: \"" + enumPair.Key + "\"},");
                }                
                writer.WriteLine("    }");
                //
                writer.WriteLine("});");
                writer.WriteLine("exports.CommandClass = CommandClass;");
                writer.WriteLine("let isCommandClassValid = function(commandClass) {");
                writer.WriteLine("    return (this.CommandClass.properties[commandClass] !== undefined);");
                writer.WriteLine("}");
                writer.WriteLine("");

                // build and output each command class's command enumeration
                foreach (var commandClassCommands in commandClassEnumerations)
                {
                    string commandClassEnumName = commandClassCommands.Key;
                    var commandEnumPairs = commandClassCommands.Value;

                    // build the command enumeration for this command class
                    writer.WriteLine("/* " + commandClassEnumName + " commands (version " + commandClassesNewestVersionTracker[commandClassEnumName] + ") */");
                    writer.WriteLine("let " + commandClassEnumName + "Command = Object.freeze({");
                    // add each command (standard lookup)
                    foreach (KeyValuePair<string, byte> enumPair in commandEnumPairs) 
                    {
                        writer.WriteLine("    " + enumPair.Key + ": 0x" + enumPair.Value.ToString("x2") + ",");
                    }
                    // add each command (reverse lookup)
                    writer.WriteLine("    properties: {");
                    foreach (KeyValuePair<string, byte> enumPair in commandEnumPairs) 
                    {
                        writer.WriteLine("        0x" + enumPair.Value.ToString("x2") + ": {name: \"" + enumPair.Key + "\"},");
                    }                
                    writer.WriteLine("    }");
                    //
                    writer.WriteLine("});");
                    writer.WriteLine("exports." + commandClassEnumName + "Command = " + commandClassEnumName + "Command;");
                    writer.WriteLine("let is" + commandClassEnumName + "CommandValid = function(command) {");
                    writer.WriteLine("    return (this." + commandClassEnumName + "Command.properties[command] !== undefined);");
                    writer.WriteLine("}");
                    writer.WriteLine("");
                }
            }
            finally
            {
                writer.Dispose();
            }

            Console.WriteLine("Done writing output file.");
        }
        finally
        {
            fileStream.Dispose();
        }

    }

    private static List<KeyValuePair<string, byte>> ParseXmlForCommandClassCommands(string shortCommandClassName, string xml) 
    {
        List<KeyValuePair<string, byte>> commandEnumPairs = new List<KeyValuePair<string, byte>>();

        XmlReader reader = XmlReader.Create(new StringReader(xml));

        while (reader.ReadToFollowing("cmd")) 
        {
            // make sure we are at depth 1 (i.e. reading the correct "cmd" entries)
            if (reader.Depth != 1)
            {
                // invalid depth; skip this entry
                continue;
            }

            // retrieve command name and version
            string commandName = reader.GetAttribute("name");
            string commandEnumValueAsString = reader.GetAttribute("key");
            // validate entries
            if (commandName == null) {
                // command name missing; skip this entry
                continue;
            }
            if (commandEnumValueAsString == null || commandEnumValueAsString.Length < 3 || commandEnumValueAsString.Substring(0, 2).ToLowerInvariant() != "0x") 
            {
                // enum value (command number) missing; skip this entry
                continue;
            }
            // try to parse the enumValue
            byte commandEnumValue;
            if (!byte.TryParse(commandEnumValueAsString.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out commandEnumValue)) 
            {
                // enum value (command number) invalid; skip this entry
                continue;
            }
            // get the short 
            string shortCommandName = ExtractShortCommandNameFromLongCommandName(shortCommandClassName, commandName);
            if (shortCommandName == null)
            {
                // if the commandName could not be reduced in size, we will ignore the command header
                shortCommandName = commandName;
            }
            // convert the command name to an upperCamelCase enumeration
            string commandEnumName = ConvertUpperCaseUnderscoreSeparatedToLowerCamelCase(shortCommandName);
            if (commandName == null) 
            {
                // command name invalid; skip this entry
                continue;
            }

            // add this command to our list
            commandEnumPairs.Add(new KeyValuePair<string, byte>(commandEnumName, commandEnumValue));
        }

        return commandEnumPairs;
    }

    private static string ExtractShortCommandClassNameFromLongCommandClassName(string name)
    {
        const string COMMAND_CLASS_PREFIX = "COMMAND_CLASS_";

        // strip the words "COMMAND_CLASS" off of the front of the command class
        if (name.IndexOf(COMMAND_CLASS_PREFIX) != 0)
        {
            // command class name invalid
            return null;
        }
        return name.Substring(COMMAND_CLASS_PREFIX.Length);
    }

    private static string ExtractShortCommandNameFromLongCommandName(string shortCommandClassName, string commandName)
    {
        string COMMAND_NAME_PREFIX = shortCommandClassName + "_";

        // strip the words "COMMAND_CLASS" off of the front of the command class
        if (commandName.IndexOf(COMMAND_NAME_PREFIX) != 0)
        {
            // command class name invalid
            return null;
        }
        return commandName.Substring(COMMAND_NAME_PREFIX.Length);
    }

    private static string ConvertUpperCaseUnderscoreSeparatedToLowerCamelCase(string name)
    {
        // convert name to UpperCamelCase
        bool useUpperCase = true;
        StringBuilder resultBuilder = new StringBuilder();
        for (Int32 offset = 0; offset < name.Length; offset++)
        {
            char ch = name[offset];
            if (char.IsLetterOrDigit(ch)) 
            {
                if (useUpperCase) 
                {
                    resultBuilder.Append(ch.ToString().ToUpperInvariant());
                    // the next character should not be uppercase
                    useUpperCase = false;
                }
                else
                {
                    resultBuilder.Append(ch.ToString().ToLowerInvariant());
                }
            }
            else 
            {
                // if this is an underscore or other separator (i.e. non-alphanumeric), the next character should be upperCase
                useUpperCase = true;
            }
        }

        // return the final result
        return resultBuilder.ToString();
    }

}
