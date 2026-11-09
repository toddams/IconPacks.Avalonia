#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Logging;

namespace IconPacks.Avalonia.Core;

public static class PackIconDataIndex
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Type, ReadOnlyDictionary<Enum, string>> Packs = new();
    private static readonly Dictionary<Enum, string> AddedIcons = new();
    private static readonly HashSet<Enum> MissingIcons = new();

    /// <summary>
    /// Adds one icon's path data. The module initializer that IconPacks.Avalonia.UsedIcons.targets
    /// generates calls it for every icon its assembly names, before any of that assembly's code runs.
    /// </summary>
    public static void Add(Enum kind, string path)
    {
        lock (Gate)
        {
            AddedIcons[kind] = path;
        }
    }

    public static ReadOnlyDictionary<Enum, string> Register(Type enumType, IDictionary<Enum, string>? icons = null)
    {
        lock (Gate)
        {
            if (!Packs.TryGetValue(enumType, out ReadOnlyDictionary<Enum, string>? value))
            {
                // Convert IDictionary<TKind, string> to IDictionary<Enum, string>
                var src = icons ?? PackIconDataFactory.Create(enumType);
                var dst = new Dictionary<Enum, string>(src.Count);
                foreach (var kv in src)
                {
                    dst[kv.Key] = kv.Value; // box TKind to Enum
                }

                value = new ReadOnlyDictionary<Enum, string>(dst);
                Packs[enumType] = value;
            }
            return value;
        }
    }

    public static bool TryGetPath(Enum kind, out string? path)
    {
        var type = kind.GetType();
        ReadOnlyDictionary<Enum, string>? packDictionary;
        lock (Gate)
        {
            if (AddedIcons.TryGetValue(kind, out path))
            {
                return true;
            }

            // The pack's own table loads only for an icon no assembly added
            if (!Packs.TryGetValue(type, out packDictionary))
            {
                packDictionary = Register(kind.GetType());
            }
        }

        if (packDictionary.TryGetValue(kind, out path))
        {
            return true;
        }

        WarnMissing(kind);
        return false;
    }

    private static void WarnMissing(Enum kind)
    {
        // Value 0 is every pack's None
        if (Convert.ToInt64(kind) == 0)
        {
            return;
        }

        bool firstMiss;
        lock (Gate)
        {
            firstMiss = MissingIcons.Add(kind);
        }

        if (firstMiss)
        {
            Logger.TryGet(LogEventLevel.Warning, "IconPacks")?.Log(
                null,
                "No path data for icon {Kind}. A pack built without its icon data only has the icons an assembly's build found: write the icon as PackIcon<Pack>Kind.<Name> in C# or XAML",
                kind);
        }
    }
}
