namespace WpAiCli.Parsing;

public sealed class ParsedOptions
{
    private readonly Dictionary<string, List<string>> _options;

    internal ParsedOptions(Dictionary<string, List<string>> options, List<string> positionals)
    {
        _options = options;
        Positionals = positionals;
    }

    public IReadOnlyList<string> Positionals { get; }

    public string? GetString(string name)
    {
        if (!_options.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        return values[^1];
    }

    public int? GetInt(string name)
    {
        var text = GetString(name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text, out var value) ? value : null;
    }

    public int[]? GetIntArray(string name)
    {
        if (!_options.TryGetValue(name, out var values) || values.Count == 0)
        {
            return null;
        }

        var list = new List<int>();
        foreach (var item in values)
        {
            foreach (var segment in item.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(segment.Trim(), out var parsed))
                {
                    list.Add(parsed);
                }
            }
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    public bool GetBool(string name, bool defaultValue)
    {
        if (!_options.TryGetValue(name, out var values) || values.Count == 0)
        {
            return defaultValue;
        }

        var last = values[^1];
        if (string.Equals(last, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(last, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return defaultValue;
    }
}

public static class OptionParser
{
    public static ParsedOptions Parse(IEnumerable<string> tokens)
    {
        var options = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        var list = tokens.ToList();

        for (var index = 0; index < list.Count; index++)
        {
            var token = list[index];
            if (!token.StartsWith("--"))
            {
                positionals.Add(token);
                continue;
            }

            string key;
            string value;

            var equalIndex = token.IndexOf('=');
            if (equalIndex > 2)
            {
                key = token[..equalIndex];
                value = token[(equalIndex + 1)..];
                if (string.IsNullOrEmpty(value))
                {
                    value = string.Empty;
                }
            }
            else
            {
                key = token;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--"))
                {
                    value = list[index + 1];
                    index++;
                }
                else
                {
                    value = "true";
                }
            }

            key = key.TrimStart('-');
            if (!options.TryGetValue(key, out var values))
            {
                values = new List<string>();
                options[key] = values;
            }
            values.Add(value);
        }

        return new ParsedOptions(options, positionals);
    }
}
