using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

/// <summary>
/// Finds the icons a project names in its C# and XAML, reads their path data from the packs'
/// Resources/Icons.json, and writes a module initializer that adds them to PackIconDataIndex.
/// RoslynCodeTaskFactory compiles it at build time against netstandard 2.0 alone, hence no System.Text.Json.
/// </summary>
public class FindUsedIcons : Microsoft.Build.Utilities.Task
{
    // PackIconMaterialKind.Folder, in C# and in {x:Static iconPacks:PackIconMaterialKind.Folder}
    private static readonly Regex KindMember = new Regex(
        @"\bPackIcon(?<pack>[A-Za-z0-9]+)Kind\s*\.\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)");

    // <iconPacks:PackIconMaterial Kind="Folder" />
    private static readonly Regex PackElement = new Regex(
        @"<[A-Za-z0-9_]+:PackIcon(?<pack>[A-Za-z0-9]+)\s[^>]*?\bKind\s*=\s*""(?<name>[A-Za-z_][A-Za-z0-9_]*)""");

    // {iconPacks:Material Folder}, {iconPacks:Material Kind=Folder}, {iconPacks:MaterialImage Kind=Folder}
    private static readonly Regex PackExtension = new Regex(
        @"\{\s*[A-Za-z0-9_]+:(?<pack>[A-Za-z0-9]+?)(?:Image)?\s+(?:Kind\s*=\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*[,}]");

    // using static IconPacks.Avalonia.Material.PackIconMaterialKind; using Icon = IconPacks.Avalonia.Material.PackIconMaterialKind;
    private static readonly Regex HiddenKind = new Regex(
        @"\busing\s+(?:static\s+(?:global::)?IconPacks\.|[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?:global::)?IconPacks\.[A-Za-z0-9_.]*Kind\s*;)");

    [Required]
    public ITaskItem[] Sources { get; set; }

    /// <summary>
    /// The folder holding the IconPacks.Avalonia.&lt;Pack&gt; projects.
    /// </summary>
    [Required]
    public string PacksDirectory { get; set; }

    [Required]
    public string OutputFile { get; set; }

    public override bool Execute()
    {
        // pack -> icon name -> where it was first seen, for warnings
        var used = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Sources)
        {
            var path = source.GetMetadata("FullPath");
            if (!scanned.Add(path) || !File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (text.IndexOf("PackIcon", StringComparison.Ordinal) < 0 && text.IndexOf("IconPacks", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var extension = Path.GetExtension(path);
            var isXaml = extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase)
                         || extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase);

            Collect(KindMember, text, path, used);
            if (isXaml)
            {
                Collect(PackElement, text, path, used);
                Collect(PackExtension, text, path, used);
            }
            else
            {
                foreach (Match hidden in HiddenKind.Matches(text))
                {
                    Log.LogWarning(null, "ICONPACKS002", null, path, LineOf(text, hidden.Index), 0, 0, 0,
                        "An icon named through '{0}' isn't found by the build and won't be embedded: write it as PackIcon<Pack>Kind.<Name>.",
                        hidden.Value.Trim());
                }
            }
        }

        var code = new StringBuilder();
        var iconCount = 0;
        foreach (var pack in used)
        {
            var dataFile = Path.Combine(PacksDirectory, "IconPacks.Avalonia." + pack.Key, "Resources", "Icons.json");
            if (!File.Exists(dataFile))
            {
                // Not an icon pack: some other type or markup extension matched
                continue;
            }

            var paths = ReadPaths(dataFile, pack.Value.Keys);
            foreach (var icon in pack.Value)
            {
                string data;
                if (!paths.TryGetValue(icon.Key, out data))
                {
                    Log.LogWarning(null, "ICONPACKS001", null, icon.Value, 0, 0, 0, 0,
                        "PackIcon{0}Kind.{1} has no path data in {2}; it won't be embedded.", pack.Key, icon.Key, dataFile);
                    continue;
                }

                code.Append("            global::IconPacks.Avalonia.Core.PackIconDataIndex.Add(global::IconPacks.Avalonia.")
                    .Append(pack.Key).Append(".PackIcon").Append(pack.Key).Append("Kind.").Append(icon.Key)
                    .Append(", \"").Append(Escape(data)).AppendLine("\");");
                iconCount++;
            }
        }

        if (iconCount == 0)
        {
            if (File.Exists(OutputFile))
            {
                File.Delete(OutputFile);
            }

            Log.LogMessage(MessageImportance.Normal, "IconPacks: no icons found");
            return !Log.HasLoggedErrors;
        }

        var file = new StringBuilder();
        file.AppendLine("// <auto-generated>");
        file.AppendLine("// The icons this assembly names in its C# and XAML, found by IconPacks.Avalonia.UsedIcons.targets.");
        file.AppendLine("// </auto-generated>");
        file.AppendLine();
        file.AppendLine("namespace IconPacks.Avalonia.UsedIcons");
        file.AppendLine("{");
        file.AppendLine("    internal static class UsedIconsModuleInitializer");
        file.AppendLine("    {");
        file.AppendLine("#pragma warning disable CA2255 // A library's icons must be added before any of its code runs");
        file.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        file.AppendLine("#pragma warning restore CA2255");
        file.AppendLine("        internal static void AddUsedIcons()");
        file.AppendLine("        {");
        file.Append(code);
        file.AppendLine("        }");
        file.AppendLine("    }");
        file.AppendLine("}");

        var content = file.ToString();
        if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutputFile));
            File.WriteAllText(OutputFile, content);
        }

        Log.LogMessage(MessageImportance.Normal, "IconPacks: embedded {0} icons from {1}", iconCount, string.Join(", ", used.Keys));
        return !Log.HasLoggedErrors;
    }

    private static void Collect(Regex pattern, string text, string path, SortedDictionary<string, SortedDictionary<string, string>> used)
    {
        foreach (Match match in pattern.Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (name == "None")
            {
                continue;
            }

            var pack = match.Groups["pack"].Value;
            SortedDictionary<string, string> names;
            if (!used.TryGetValue(pack, out names))
            {
                names = new SortedDictionary<string, string>(StringComparer.Ordinal);
                used.Add(pack, names);
            }

            if (!names.ContainsKey(name))
            {
                names.Add(name, path);
            }
        }
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Reads the wanted entries of an Icons.json, a flat object of icon name to path data,
    /// without decoding the other values.
    /// </summary>
    private static Dictionary<string, string> ReadPaths(string dataFile, ICollection<string> wanted)
    {
        var json = File.ReadAllText(dataFile);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = SkipWhitespace(json, 0);
        Expect(json, ref i, '{', dataFile);
        i = SkipWhitespace(json, i);
        if (json[i] == '}')
        {
            return paths;
        }

        while (true)
        {
            var key = ReadString(json, ref i, dataFile);
            i = SkipWhitespace(json, i);
            Expect(json, ref i, ':', dataFile);
            i = SkipWhitespace(json, i);
            if (wanted.Contains(key))
            {
                paths[key] = ReadString(json, ref i, dataFile);
            }
            else
            {
                SkipString(json, ref i, dataFile);
            }

            i = SkipWhitespace(json, i);
            if (json[i] == '}')
            {
                return paths;
            }

            Expect(json, ref i, ',', dataFile);
            i = SkipWhitespace(json, i);
        }
    }

    private static int SkipWhitespace(string json, int i)
    {
        while (i < json.Length && char.IsWhiteSpace(json[i]))
        {
            i++;
        }

        if (i == json.Length)
        {
            throw new FormatException("Unexpected end of JSON");
        }

        return i;
    }

    private static void Expect(string json, ref int i, char expected, string dataFile)
    {
        if (json[i] != expected)
        {
            throw new FormatException(string.Format("Expected '{0}' at offset {1} of {2}", expected, i, dataFile));
        }

        i++;
    }

    private static void SkipString(string json, ref int i, string dataFile)
    {
        Expect(json, ref i, '"', dataFile);
        while (json[i] != '"')
        {
            i += json[i] == '\\' ? 2 : 1;
        }

        i++;
    }

    private static string ReadString(string json, ref int i, string dataFile)
    {
        Expect(json, ref i, '"', dataFile);
        var value = new StringBuilder();
        while (json[i] != '"')
        {
            var c = json[i++];
            if (c != '\\')
            {
                value.Append(c);
                continue;
            }

            var escaped = json[i++];
            switch (escaped)
            {
                case 'b': value.Append('\b'); break;
                case 'f': value.Append('\f'); break;
                case 'n': value.Append('\n'); break;
                case 'r': value.Append('\r'); break;
                case 't': value.Append('\t'); break;
                case 'u':
                    value.Append((char)Convert.ToInt32(json.Substring(i, 4), 16));
                    i += 4;
                    break;
                default: value.Append(escaped); break;
            }
        }

        i++;
        return value.ToString();
    }

    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '"' || c == '\\')
            {
                escaped.Append('\\').Append(c);
            }
            else if (c < ' ')
            {
                escaped.Append("\\u").Append(((int)c).ToString("x4"));
            }
            else
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }
}
