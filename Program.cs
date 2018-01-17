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
    // INSTRUCTIONS: set the following three values to the desired output language, the xml input filepath and the desired output directory
    const OutputLanguage OUTPUT_LANGUAGE = OutputLanguage.JavaScript;
    const string XML_INPUT_FILE_PATH = "./input/ZWave_custom_cmd_classes.xml";
    // NOTE: the output directory is automatically purged, so be very careful when changing the output directory path (or disable the File.Delete functionality)
    const string OUTPUT_DIRECTORY_PATH = "./output/";

    //

    private enum OutputLanguage 
    {
        Java,
        JavaScript,
    }

    static void Main(string[] args)
    {
        Console.WriteLine("Loading XML input file: " + XML_INPUT_FILE_PATH);

        // create a list which will hold our command classes' name/value enumeration pairs
        SortedDictionary<string, byte> commandClassEnumDictionary = new SortedDictionary<string, byte>();
        // create a dictionary which will keep track of the latest version number of each command class enum (with the enum value as the key...to eliminate class "renaming" issues)
        Dictionary<byte, byte> commandClassNewestVersionLookup = new Dictionary<byte, byte>();
        //
        // create a sorted dictionary of commands for each command class (storing only the entries for the latest command class version--which should be a superset of earlier versions)
        SortedDictionary<string, List<KeyValuePair<string, byte>>> commandClassEnumerations = new SortedDictionary<string, List<KeyValuePair<string, byte>>>();

        /*** STEP 1 (FRONT END): READ COMMAND CLASSES AND THEIR COMMANDS FROM XML FILE ***/

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
                    // make sure we are at depth (level) 1 (i.e. reading the correct "cmd_class" entries)
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
                    // try to parse the command class's enumeration value and version
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
                    // determine the command class name formatting: either as-is (in languages like Java with UPPER_CASE_CONSTANTS) or in special casing (for languages like JavaScript)
                    string commandClassEnumName = null;
                    switch (OUTPUT_LANGUAGE)
                    {
                        case OutputLanguage.Java:
                            {
                                // create a non-ambiguous reference variable for the shortened command class name
                                commandClassEnumName = commandClassShortName;
                                // verify that the shortened command name can be converted to UpperCamelCase (since this will be required during the source file writing phase)
                                if (ConvertUpperCaseUnderscoreSeparatedToUpperCamelCase(commandClassShortName) == null)
                                {
                                    // command class name invalid; skip this entry
                                    continue;
                                }
                            }
                            break;
                        case OutputLanguage.JavaScript:
                            {
                                // conver the shortened command class name to UpperCamelCase and store it in the "enum name" reference
                                commandClassEnumName = ConvertUpperCaseUnderscoreSeparatedToUpperCamelCase(commandClassShortName);                                
                            }
                            break;
                        default:
                            throw new Exception("Output language not supported.");
                    }
                    //
                    if (commandClassEnumName == null) 
                    {
                        // command class name invalid; skip this entry
                        continue;
                    }

                    // get the newest already-stored version of the command class, if it was already added to our enumeration
                    // NOTE: we do this immediately before adding the command class and initial version to the list, so that we don't capture the just-added version
                    // NOTE: we look up version #s based on the class's enum value--instead of the class's name--because command class names can change over time
                    byte? newestVersion = null;
                    if (commandClassNewestVersionLookup.ContainsKey(commandClassEnumValue))
                    {
                        newestVersion = commandClassNewestVersionLookup[commandClassEnumValue];
                    }
                    //
                    // if an earlier version of the command class already exists, remove it now
                    if (newestVersion != null && newestVersion < commandClassVersion)
                    {
                        // find the class name of the previously-stored command class
                        string oldCommandClassKey = null;
                        //
                        int commandClassKeysCount = commandClassEnumDictionary.Count;
                        string[] commandClassKeys = new string[commandClassKeysCount];
                        commandClassEnumDictionary.Keys.CopyTo(commandClassKeys, 0);
                        //
                        for(int iCommandClass = 0; iCommandClass < commandClassKeysCount; iCommandClass++)
                        {
                            if (commandClassEnumDictionary[commandClassKeys[iCommandClass]] == commandClassEnumValue)
                            {
                                oldCommandClassKey = commandClassKeys[iCommandClass];
                            }
                        }

                        // remove the version of the previously-stored command class from our version lookup dictionary
                        commandClassNewestVersionLookup.Remove(commandClassEnumValue);
                        //
                        // remove the older command class from the "command class enum" dictionary
                        if (commandClassEnumDictionary.ContainsKey(oldCommandClassKey)) 
                        {
                            commandClassEnumDictionary.Remove(oldCommandClassKey);
                        }
                        //
                        // remove the older command class from the "command enums for each command class" dictionary
                        if (commandClassEnumerations.ContainsKey(oldCommandClassKey)) 
                        {
                            commandClassEnumerations.Remove(oldCommandClassKey);
                        }
                    }

                    // if our current data is either a new command class or a newer version of an already-stored command class, add it now
                    if (newestVersion == null || newestVersion < commandClassVersion)
                    {
                        // add the command class to our "command class enum"
                        commandClassEnumDictionary.Add(commandClassEnumName, commandClassEnumValue);
                        //
                        // and also add the corresponding version lookup entry
                        commandClassNewestVersionLookup.Add(commandClassEnumValue, commandClassVersion);
                        
                        // now, create an enumeration for the command class's commands (derived from the command class xml node's inner xml)
                        string commandClassCommandsEnumName = commandClassEnumName + "Command";
                        List<KeyValuePair<string, byte>> commandEnumPairs = ParseXmlForCommandClassCommands(commandClassShortName, reader.ReadOuterXml(), OUTPUT_LANGUAGE);
                        
                        // add the command class's command enumeration values
                        commandClassEnumerations.Add(commandClassEnumName, commandEnumPairs);
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

        /*** STEP 2 (BACK END): WRITE COMMAND CLASSES AND COMMANDS AS ENUMERATIONS TO SOURCE FILES ***/

        // clear out the output directory
        foreach(var fileToDelete in Directory.EnumerateFiles(OUTPUT_DIRECTORY_PATH))
        {
            Console.WriteLine("Deleting old output file: " + fileToDelete);
            File.Delete(fileToDelete);
        }

        // generate the new output files
        switch (OUTPUT_LANGUAGE)
        {
            case OutputLanguage.Java:
                // generate Java enum source files
                GenerateJavaEnumFiles(OUTPUT_DIRECTORY_PATH, commandClassEnumDictionary, commandClassEnumerations, commandClassNewestVersionLookup);
                break;
            case OutputLanguage.JavaScript:
                // generate JavaScript enum source files
                GenerateJavaScriptEnumFiles(OUTPUT_DIRECTORY_PATH, commandClassEnumDictionary, commandClassEnumerations, commandClassNewestVersionLookup);
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private static List<KeyValuePair<string, byte>> ParseXmlForCommandClassCommands(string shortCommandClassName, string xml, OutputLanguage outputLanguage) 
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
            // get the short command name
            string shortCommandName = ExtractShortCommandNameFromLongCommandName(shortCommandClassName, commandName);
            if (shortCommandName == null)
            {
                // if the commandName could not be reduced in size, we will ignore the command header
                shortCommandName = commandName;
            }
            // store the command class name as-is (in languages like Java with UPPER_CASE_CONSTANTS) or convert it to special casing (for languages like JavaScript)
            string commandEnumName = null;
            switch(outputLanguage)
            {
                case OutputLanguage.Java:
                    {
                        // store the short command name as-is
                        commandEnumName = shortCommandName;
                    }
                    break;
                case OutputLanguage.JavaScript:
                    {
                        // convert the command name to a UpperCamelCase enumeration
                        commandEnumName = ConvertUpperCaseUnderscoreSeparatedToUpperCamelCase(shortCommandName);
                    }
                    break;
                default:
                    throw new ArgumentException("Output language is not supported.", nameof(outputLanguage));
            }
            //
            if (commandEnumName == null) 
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
        return ConvertUpperCaseUnderscoreSeparatedToCamelCase(name, false);
    }

    private static string ConvertUpperCaseUnderscoreSeparatedToUpperCamelCase(string name)
    {
        return ConvertUpperCaseUnderscoreSeparatedToCamelCase(name, true);
    }

    private static string ConvertUpperCaseUnderscoreSeparatedToCamelCase(string name, bool capitalizeFirstLetter)
    {
        // if the user has requested UpperCamelCase (capitalizeFirstLetter = true), set our useUpperCase flag to capitilize the first letter
        bool useUpperCase = capitalizeFirstLetter;

        // convert name to lowerCamelCase/UpperCamelCase
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

    private static void GenerateJavaEnumFiles(
        string directoryPath, 
        SortedDictionary<string, byte> commandClassEnumDictionary, 
        SortedDictionary<string, List<KeyValuePair<string, byte>>> commandClassEnumerations,
        Dictionary<byte, byte> commandClassVersionLookup)
    {
        const string JAVA_OUTPUT_PACKAGE_NAME = "com.zwavepublic.zwaveip.commands";

        // create the main CommandClass.java enumeration file
        String outputFilePath;
        outputFilePath = directoryPath + "CommandClass.java";

        Console.WriteLine("Writing Java output file: " + outputFilePath);

        FileStream fileStream = null;
        try 
        {
            fileStream = new FileStream(outputFilePath, FileMode.Create);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Could not open file: " + outputFilePath);
            Console.WriteLine("exception: " + ex.Message);
            return;
        }

        try
        {
            StreamWriter writer = new StreamWriter(fileStream);

            try     
            {
                // build and output the command class enumeration
                writer.WriteLine("package " + JAVA_OUTPUT_PACKAGE_NAME + ";");
                writer.WriteLine("");
                writer.WriteLine("import java.util.HashMap;");
                writer.WriteLine("");
                writer.WriteLine("/* Z-Wave command classes */");
                writer.WriteLine("public enum CommandClass {");
                // add each command class (standard lookup)
                int commandClassKeysCount = commandClassEnumDictionary.Count;
                string[] commandClassKeys = new string[commandClassKeysCount];
                commandClassEnumDictionary.Keys.CopyTo(commandClassKeys, 0);
                for (int iKey = 0; iKey < commandClassKeysCount; iKey += 1)
                {
                    string key = commandClassKeys[iKey];
                    byte value;
                    if (commandClassEnumDictionary.TryGetValue(key, out value) == false)
                    {
                        throw new InvalidProgramException();
                    }
                    //
                    writer.Write("    " + key + "(0x" + value.ToString("x2") + ")");
                    if (iKey < commandClassKeysCount - 1) 
                    {
                        writer.WriteLine(",");
                    }
                    else
                    {
                        writer.WriteLine(";");
                    }
                }
                // add internal class-scope plumbing for reverse lookup
                writer.WriteLine("");
                writer.WriteLine("    private static final HashMap<Integer, CommandClass> _map = new HashMap<Integer, CommandClass>();");
                writer.WriteLine("    static {");
                writer.WriteLine("        for (CommandClass value: CommandClass.values()) {");
                writer.WriteLine("            _map.put(value.intValue(), value);");
                writer.WriteLine("        }");
                writer.WriteLine("    }");
                // add internal plumbing for storing and returning the integer value of each enumeration constant
                writer.WriteLine("");
                writer.WriteLine("    private int _intValue;");
                writer.WriteLine("");
                writer.WriteLine("    private CommandClass(int value) {");
                writer.WriteLine("        this._intValue = value;");
                writer.WriteLine("    }");
                writer.WriteLine("");
                writer.WriteLine("    public int intValue() {");
                writer.WriteLine("        return this._intValue;");
                writer.WriteLine("    }");
                // add reverse lookup (for both the standard exception-throwing lookup and a null-returning "IfPresent" variant)
                writer.WriteLine("");
                writer.WriteLine("    public static CommandClass valueOf(int intValue) {");
                writer.WriteLine("        CommandClass result = _map.get(intValue);");
                writer.WriteLine("        if(result == null) {");
                writer.WriteLine("            throw new IllegalArgumentException();");
                writer.WriteLine("        } else {");
                writer.WriteLine("            return result;");
                writer.WriteLine("        }");
                writer.WriteLine("    }");
                writer.WriteLine("");
                writer.WriteLine("    public static CommandClass valueOfIfPresent(int intValue) {");
                writer.WriteLine("        return _map.get(intValue);");
                writer.WriteLine("    }");
                //
                writer.WriteLine("}");
                writer.WriteLine("");
            }
            finally
            {
                writer.Dispose();
            }

            Console.WriteLine("Done writing output file: " + outputFilePath);
        }
        finally
        {
            fileStream.Dispose();
        }

        // build the "Command" interface which our command classes will implement; this interface provides the developer with better type and command range safety 
        outputFilePath = directoryPath + "Command.java";

        Console.WriteLine("Writing Java output file: " + outputFilePath);

        fileStream = null;
        try 
        {
            fileStream = new FileStream(outputFilePath, FileMode.Create);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Could not open file: " + outputFilePath);
            Console.WriteLine("exception: " + ex.Message);
            return;
        }

        try
        {
            StreamWriter writer = new StreamWriter(fileStream);

            try     
            {
                // build and output the command class enumeration
                writer.WriteLine("package " + JAVA_OUTPUT_PACKAGE_NAME + ";");
                writer.WriteLine("");
                writer.WriteLine("/* Interface for Z-Wave command enumerations */");
                writer.WriteLine("public interface Command {");
                writer.WriteLine("    public int intValue();");
                writer.WriteLine("}");
                writer.WriteLine("");
            }
            finally
            {
                writer.Dispose();
            }

            Console.WriteLine("Done writing output file: " + outputFilePath);
        }
        finally
        {
            fileStream.Dispose();
        }

        // build and output each command class's command enumeration
        foreach (var commandClassCommands in commandClassEnumerations)
        {
            string commandClassEnumName = commandClassCommands.Key;
            string commandClassEnumNameInUpperCamelCase = ConvertUpperCaseUnderscoreSeparatedToUpperCamelCase(commandClassEnumName);
            var commandEnumPairs = commandClassCommands.Value;

            byte commentClassVersion;
            if (commandClassEnumDictionary.TryGetValue(commandClassEnumName, out commentClassVersion) == false)
            {
                throw new InvalidProgramException();
            }

            // if this command class has no commands, it should have no enum: skip it.
            if(commandEnumPairs.Count == 0) {
                continue;
            }

            outputFilePath = directoryPath + commandClassEnumNameInUpperCamelCase + "Command.java";

            Console.WriteLine("Writing Java output file: " + outputFilePath);

            fileStream = null;
            try 
            {
                fileStream = new FileStream(outputFilePath, FileMode.Create);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Could not open file: " + outputFilePath);
                Console.WriteLine("exception: " + ex.Message);
                return;
            }

            try
            {
                StreamWriter writer = new StreamWriter(fileStream);

                try     
                {
                    byte commandClassEnumValue = commandClassEnumDictionary[commandClassEnumName];

                    // build the command enumeration for this command class
                    writer.WriteLine("package " + JAVA_OUTPUT_PACKAGE_NAME + ";");
                    writer.WriteLine("");
                    writer.WriteLine("import java.util.HashMap;");
                    writer.WriteLine("");
                    writer.WriteLine("/* " + commandClassEnumNameInUpperCamelCase + " commands (version " + commandClassVersionLookup[commandClassEnumValue] + ") */");
                    writer.WriteLine("public enum " + commandClassEnumNameInUpperCamelCase + "Command implements com.zwavepublic.zwaveip.commands.Command {");
                    // add each command (standard lookup)
                    int commandCount = commandEnumPairs.Count;
                    for (int iKey = 0; iKey < commandCount; iKey += 1)
                    {
                        string key = commandEnumPairs[iKey].Key;
                        byte value = commandEnumPairs[iKey].Value;
                        //
                        writer.Write("    " + key + "(0x" + value.ToString("x2") + ")");
                        if (iKey < commandCount - 1) 
                        {
                            writer.WriteLine(",");
                        }
                        else
                        {
                            writer.WriteLine(";");
                        }
                    }
                    // add internal class-scope plumbing for reverse lookup
                    writer.WriteLine("");
                    writer.WriteLine("    private static final HashMap<Integer, " + commandClassEnumNameInUpperCamelCase + "Command> _map = new HashMap<Integer, " + commandClassEnumNameInUpperCamelCase + "Command>(" + commandCount.ToString() + ");");
                    writer.WriteLine("    static {");
                    writer.WriteLine("        for (" + commandClassEnumNameInUpperCamelCase + "Command value: " + commandClassEnumNameInUpperCamelCase + "Command.values()) {");
                    writer.WriteLine("            _map.put(value.intValue(), value);");
                    writer.WriteLine("        }");
                    writer.WriteLine("    }");
                    // add internal plumbing for storing and returning the integer value of each enumeration constant
                    writer.WriteLine("");
                    writer.WriteLine("    private int _intValue;");
                    writer.WriteLine("");
                    writer.WriteLine("    private " + commandClassEnumNameInUpperCamelCase + "Command(int value) {");
                    writer.WriteLine("        this._intValue = value;");
                    writer.WriteLine("    }");
                    // add override(s) for Command interface
                    writer.WriteLine("");
                    writer.WriteLine("    @Override");
                    writer.WriteLine("    public int intValue() {");
                    writer.WriteLine("        return this._intValue;");
                    writer.WriteLine("    }");
                    // add reverse lookup
                    writer.WriteLine("");
                    writer.WriteLine("    public static " + commandClassEnumNameInUpperCamelCase + "Command valueOf(int intValue) {");
                    writer.WriteLine("        " + commandClassEnumNameInUpperCamelCase + "Command result = _map.get(intValue);");
                    writer.WriteLine("        if(result == null) {");
                    writer.WriteLine("            throw new IllegalArgumentException();");
                    writer.WriteLine("        } else {");
                    writer.WriteLine("            return result;");
                    writer.WriteLine("        }");
                    writer.WriteLine("    }");
                    writer.WriteLine("");
                    writer.WriteLine("    public static " + commandClassEnumNameInUpperCamelCase + "Command valueOfIfPresent(int intValue) {");
                    writer.WriteLine("        return _map.get(intValue);");
                    writer.WriteLine("    }");
                    //
                    writer.WriteLine("}");
                    writer.WriteLine("");
                }
                finally
                {
                    writer.Dispose();
                }

                Console.WriteLine("Done writing output file: " + outputFilePath);
            }
            finally
            {
                fileStream.Dispose();
            }
        }
    }

    private static void GenerateJavaScriptEnumFiles(
        string directoryPath, 
        SortedDictionary<string, byte> commandClassEnumDictionary, 
        SortedDictionary<string, List<KeyValuePair<string, byte>>> commandClassEnumerations,
        Dictionary<byte, byte> commandClassVersionLookup)
    {
        const string JAVASCRIPT_OUTPUT_FILE_NAME = "zwave_command_classes.js";                    

        String outputFilePath;
        outputFilePath = directoryPath + JAVASCRIPT_OUTPUT_FILE_NAME;

        Console.WriteLine("Writing JavaScript output file: " + outputFilePath);

        FileStream fileStream = null;
        try 
        {
            fileStream = new FileStream(outputFilePath, FileMode.Create);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Could not open file: " + outputFilePath);
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
                foreach (KeyValuePair<string, byte> enumPair in commandClassEnumDictionary) 
                {
                    writer.WriteLine("    " + enumPair.Key + ": 0x" + enumPair.Value.ToString("x2") + ",");
                }
                // add each command class (reverse lookup)
                writer.WriteLine("    properties: {");
                foreach (KeyValuePair<string, byte> enumPair in commandClassEnumDictionary) 
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
                    byte commandClassEnumValue = commandClassEnumDictionary[commandClassEnumName];
                    var commandEnumPairs = commandClassCommands.Value;

                    // if this command class has no commands, it should have no enum: skip it.
                    if(commandEnumPairs.Count == 0) {
                        continue;
                    }

                    // build the command enumeration for this command class
                    writer.WriteLine("/* " + commandClassEnumName + " commands (version " + commandClassVersionLookup[commandClassEnumValue] + ") */");
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
}
