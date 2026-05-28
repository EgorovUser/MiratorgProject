using System;
using System.Collections.Generic;

namespace CommonLib
{
    public static class DictProcessor
    {
        public static string ExtractRequiredString(IDictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value) || !(value is string strValue) || string.IsNullOrWhiteSpace(strValue))
                throw new ArgumentException($"В словаре отсутствует ключ '{key}' или его значение является пустой строкой.");
            return strValue;
        }

        public static string ExtractOptionalString(IDictionary<string, object> parameters, string key)
        {
            if (parameters.TryGetValue(key, out var value) && value is string strValue && !string.IsNullOrWhiteSpace(strValue))
                return strValue;
            return null;
        }

        public static bool ExtractOptionalBool(IDictionary<string, object> parameters, string key, bool defaultValue)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is bool b)
                    return b;
                if (value is string s && bool.TryParse(s, out bool parsed))
                    return parsed;
            }
            return defaultValue;
        }

        public static char ExtractDelimiter(IDictionary<string, object> parameters, object defaultValue)
        {
            if (parameters.TryGetValue("Delimiter", out var value))
            {
                if (value is char c)
                    return c;
                if (value is string s && s.Length == 1)
                    return s[0];
                throw new ArgumentException("Значение для ключа 'Delimiter' должно быть символом или строкой длиной 1.");
            }
            return ';';
        }
    }
}
