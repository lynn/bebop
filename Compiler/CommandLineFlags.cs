﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Generators;
using Core.Logging;
using Core.Meta;

namespace Compiler
{
    #region FlagAttribute

    /// <summary>
    ///     Models an application commandline flag.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class CommandLineFlagAttribute : Attribute
    {
        /// <summary>
        ///     Creates a new commandline flag attribute
        /// </summary>
        /// <param name="name">The name of the commandline flag.</param>
        /// <param name="helpText">A detailed description of flag.</param>
        /// <param name="usageExample">An example of how to use the attributed flag.</param>
        /// <param name="isGeneratorFlag">Indicates if a flag is used to generate code.</param>
        public CommandLineFlagAttribute(string name, string helpText, string usageExample = "", bool isGeneratorFlag = false)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (string.IsNullOrWhiteSpace(helpText))
            {
                throw new ArgumentNullException(nameof(helpText));
            }
            Name = name;
            HelpText = helpText;
            UsageExample = usageExample;
            IsGeneratorFlag = isGeneratorFlag;
        }

        /// <summary>
        ///     The name commandline flag. This name is usually a single english word.
        /// </summary>
        /// <remarks>
        ///     For compound words you should use a hyphen separator rather than camel casing.
        /// </remarks>
        public string Name { get; }

        /// <summary>
        ///     A detailed description of the commandline flag.
        /// </summary>
        public string HelpText { get; }

        /// <summary>
        ///     If any an example of the parameter that is used in conjunction with the flag
        /// </summary>
        public string UsageExample { get; }
        /// <summary>
        /// If this property is set to true the attributed commandline flag is used to instantiate a code generator. 
        /// </summary>
        public bool IsGeneratorFlag { get; }
    }

#endregion

    /// <summary>
    /// A class for constructing and parsing all available commands.
    /// </summary>
    public class CommandLineFlags
    {
        /// <summary>
        /// The name of the config file used by bebopc.
        /// </summary>
        private const string ConfigFileName = "bebop.json";

        [CommandLineFlag("config", "Initializes the compiler from the specified configuration file.", "--config bebop.json")]
        public string? ConfigFile { get; private set; }

        [CommandLineFlag("cs", "Generate C# source code to the specified file", "--cs ./cowboy/bebop/HelloWorld.cs", true)]
        public string? CSharpOutput { get; private set; }
        [CommandLineFlag("ts", "Generate TypeScript source code to the specified file", "--ts ./cowboy/bebop/HelloWorld.ts", true)]
        public string? TypeScriptOutput { get; private set; }
        [CommandLineFlag("dart", "Generate Dart source code to the specified file", "--ts ./cowboy/bebop/HelloWorld.dart", true)]
        public string? DartOutput { get; private set; }

        [CommandLineFlag("namespace", "When this option is specified generated code will use namespaces", "--lang cs --namespace [package]")]
        public string? Namespace { get; private set; }

        [CommandLineFlag("dir", "Parse and generate code from a directory of schemas", "--lang ts --dir [input dir]")]
        public string? SchemaDirectory { get; private set; }

        [CommandLineFlag("files", "Parse and generate code from a list of schemas", "--files [file1] [file2] ...")]
        public List<string>? SchemaFiles { get; private set; }

        [CommandLineFlag("check", "Only check a given schema is valid", "--check [file.bop] [file2.bop] ...")]
        public List<string>? CheckSchemaFiles { get; private set; }

        /// <summary>
        /// When set to true the process will output the product version and exit with a zero return code.
        /// </summary>
        [CommandLineFlag("version", "Show version info and exit.", "--version")]
        public bool Version { get; private set; }

        /// <summary>
        /// When set to true the process will output the <see cref="HelpText"/> and exit with a zero return code.
        /// </summary>
        [CommandLineFlag("help", "Show this text and exit.", "--help")]
        public bool Help { get; private set; }

        /// <summary>
        ///     Controls how loggers format data.
        /// </summary>
        [CommandLineFlag("log-format", "Defines the formatter that will be used with logging.", "--formatter (structured|msbuild)")]
        public LogFormatter LogFormatter { get; private set; }

        public string HelpText { get; private init; }

        /// <summary>
        /// Returns the alias and output file of all commandline specified code generators.
        /// </summary>
        public IEnumerable<(string Alias, string OutputFile)> GetParsedGenerators()
        {
            var props = (from p in typeof(CommandLineFlags).GetProperties()
                let attr = p.GetCustomAttributes(typeof(CommandLineFlagAttribute), true)
                where attr.Length == 1
                select new { Property = p, Attribute = attr.First() as CommandLineFlagAttribute }).ToList();

            foreach (var flag in props)
            {
                if (flag.Attribute.IsGeneratorFlag && flag.Property.GetValue(this) is string value)
                {
                    yield return (flag.Attribute.Name, value);
                }
            }
        }

        #region Static


        /// <summary>
        ///     Hide the constructor to prevent direct initialization
        /// </summary>
        private CommandLineFlags(string helpText)
        {
            HelpText = helpText;
        }

        /// <summary>
        /// Searches recursively upward to locate the config file belonging to <see cref="ConfigFileName"/>.
        /// </summary>
        /// <returns>The fully qualified path to the config file, or null if not found.</returns>
        public static string? FindBebopConfig()
        {
            var workingDirectory = Directory.GetCurrentDirectory();
            var configFile = Directory.GetFiles(workingDirectory, ConfigFileName).FirstOrDefault();
            while (string.IsNullOrWhiteSpace(configFile))
            {
                if (Directory.GetParent(workingDirectory) is not {Exists: true} parent)
                {
                    break;
                }
                workingDirectory = parent.FullName;
                if (parent.GetFiles(ConfigFileName)?.FirstOrDefault() is {Exists: true} file)
                {
                    configFile = file.FullName;
                }
            }
            return configFile;
        }

    #endregion

    #region Parsing

        /// <summary>
        ///     Parses an array of commandline flags into dictionary.
        /// </summary>
        /// <param name="args">The flags to be parsed.</param>
        /// <returns>A dictionary containing all parsed flags and their value if any.</returns>
        private static Dictionary<string, string> GetFlags(string[] args)
        {
            var arguments = new Dictionary<string, string>();
            foreach (var token in args)
            {
                if (token.StartsWith("--"))
                {
                    var key = new string(token.SkipWhile(c => c == '-').ToArray()).ToLowerInvariant();
                    var value = string.Join(" ", args.SkipWhile(i => i != $"--{key}").Skip(1).TakeWhile(i => !i.StartsWith("--")));
                    arguments.Add(key, value);
                }
            }
            return arguments;
        }

        /// <summary>
        /// Attempts to find the <see cref="LogFormatter"/> flag and parse it's value.
        /// </summary>
        /// <param name="args">The commandline arguments to sort through</param>
        /// <returns>If the <see cref="LogFormatter"/> flag was present an had a valid value, that enum member will be returned. Otherwise the default formatter is used.</returns>
        public static LogFormatter FindLogFormatter(string[] args)
        {
            var flags = GetFlags(args);
            foreach (var flag in flags)
            {
                if (flag.Key.Equals("log-format", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<Core.Logging.LogFormatter>(flag.Value, true, out var parsedEnum))
                {
                    return parsedEnum;
                }
            }
            return LogFormatter.Structured;
        }


        /// <summary>
        ///     Attempts to parse commandline flags into a <see cref="CommandLineFlags"/> instance
        /// </summary>
        /// <param name="args">the array of arguments to parse</param>
        /// <param name="flagStore">An instance which contains all parsed flags and their values</param>
        /// <param name="errorMessage">A human-friendly message describing why parsing failed.</param>
        /// <returns>
        ///     If the provided
        ///     <param name="args"></param>
        ///     were parsed this method returns true.
        /// </returns>
        public static bool TryParse(string[] args, out CommandLineFlags flagStore, out string errorMessage)
        {
            errorMessage = string.Empty;
            var props = (from p in typeof(CommandLineFlags).GetProperties()
                let attr = p.GetCustomAttributes(typeof(CommandLineFlagAttribute), true)
                where attr.Length == 1
                select new {Property = p, Attribute = attr.First() as CommandLineFlagAttribute}).ToList();

            var stringBuilder = new IndentedStringBuilder();

            stringBuilder.AppendLine("Usage:");
            stringBuilder.Indent(4);
            foreach (var prop in props.Where(prop => !string.IsNullOrWhiteSpace(prop.Attribute.UsageExample)))
            {
                stringBuilder.AppendLine($"{ReservedWords.CompilerName} {prop.Attribute.UsageExample}");
            }
            stringBuilder.Dedent(4);

            stringBuilder.AppendLine(string.Empty);
            stringBuilder.AppendLine(string.Empty);
            stringBuilder.AppendLine("Options:");
            stringBuilder.Indent(4);
            foreach (var prop in props)
            {
                stringBuilder.AppendLine($"--{prop.Attribute.Name}  {prop.Attribute.HelpText}");
            }

            flagStore = new CommandLineFlags(helpText: stringBuilder.ToString());

            var parsedFlags = GetFlags(args);
           
            if (parsedFlags.Count == 0)
            {
                errorMessage = "No commandline flags found.";
                return false;
            }

            if (parsedFlags.ContainsKey("help"))
            {
                flagStore.Help = true;
                return true;
            }

            if (parsedFlags.ContainsKey("version"))
            {
                flagStore.Version = true;
                return true;
            }

            foreach (var flag in props)
            {

                if (!parsedFlags.ContainsKey(flag.Attribute.Name))
                {
                    continue;
                }

                var parsedValue = parsedFlags[flag.Attribute.Name]?.Trim();
                var propertyType = flag.Property.PropertyType;
                if (propertyType == typeof(bool))
                {
                    flag.Property.SetValue(flagStore, true);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(parsedValue))
                {
                    errorMessage = $"Commandline flag '{flag.Attribute.Name}' was not assigned a value.";
                    return false;
                }
                if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    Type itemType = propertyType.GetGenericArguments()[0];
                    if (!(Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType)) is IList genericList))
                    {
                        errorMessage = $"Failed to activate '{flag.Property.Name}'.";
                        return false;
                    }
                    foreach (var item in parsedValue.Split(" ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        genericList.Add(Convert.ChangeType(item.Trim(), itemType));
                    }
                    flag.Property.SetValue(flagStore, genericList, null);
                } else  if (propertyType.IsEnum)
                {
                    if (!Enum.TryParse(propertyType, parsedValue, true, out var parsedEnum))
                    {
                        errorMessage = $"Failed to parse '{parsedValue}' into a member of '{propertyType}'.";
                        return false;
                    }
                    flag.Property.SetValue(flagStore, parsedEnum, null);
                }
                else
                {
                    flag.Property.SetValue(flagStore, Convert.ChangeType(parsedValue, flag.Property.PropertyType),
                        null);
                }
            }
            errorMessage = string.Empty;
            return true;
        }
    }

#endregion
}
