using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Platform;

namespace IconPacks.Avalonia.Core
{
    public static class PackIconDataFactory
    {
        public static IDictionary<TEnum, string> Create<TEnum>() where TEnum : struct, Enum
        {
            var iconsUri = IconsUri(typeof(TEnum));
            if (!AssetLoader.Exists(iconsUri))
            {
                return new Dictionary<TEnum, string>();
            }

            using var iconJsonStream = AssetLoader.Open(iconsUri);
            var stringDict = JsonSerializer.Deserialize(
                                 iconJsonStream, IconJsonContext.Default.DictionaryStringString) 
                             ?? new Dictionary<string, string>();

            var result = new Dictionary<TEnum, string>(stringDict.Count);
            foreach (var kvp in stringDict)
            {
                if (Enum.TryParse<TEnum>(kvp.Key, out var enumKey))
                {
                    result[enumKey] = kvp.Value;
                }
            }

            return result;
        }
        
        public static IDictionary<Enum, string> Create(Type enumType)
        {
            var iconsUri = IconsUri(enumType);
            if (!AssetLoader.Exists(iconsUri))
            {
                return new Dictionary<Enum, string>();
            }

            using var iconJsonStream = AssetLoader.Open(iconsUri);
            var stringDict = JsonSerializer.Deserialize<Dictionary<string, string>>(
                                 iconJsonStream, IconJsonContext.Default.DictionaryStringString) 
                             ?? new Dictionary<string, string>();

            var result = new Dictionary<Enum, string>(stringDict.Count);
            foreach (var kvp in stringDict)
            {
                try
                {
                    var enumKey = Enum.Parse(enumType, kvp.Key);
                    result[(Enum)enumKey] = kvp.Value;
                }
                catch
                {
                    // ignored
                }
            }

            return result;
        }

        // A pack built without IconPacksEmbedIconData=true has no Icons.json
        private static Uri IconsUri(Type enumType)
        {
            return new Uri($"avares://{enumType.Assembly.GetName().Name}/Resources/Icons.json");
        }
    }
}