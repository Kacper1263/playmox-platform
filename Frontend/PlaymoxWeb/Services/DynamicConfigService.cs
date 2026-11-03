using System.Text.Json;
using PlaymoxWeb.Models;

namespace PlaymoxWeb.Services
{
    public class DynamicConfigService
    {
        private readonly LocalizationService _localization;

        public DynamicConfigService(LocalizationService localization)
        {
            _localization = localization;
        }

        public List<DynamicConfigField> ParseDynamicConfigFields(GameDefinitionDto? definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.JsonSchema))
            {
                return [];
            }

            try
            {
                using var schema = JsonDocument.Parse(definition.JsonSchema);
                if (!schema.RootElement.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                {
                    return [];
                }

                var result = new List<DynamicConfigField>();
                foreach (var property in properties.EnumerateObject())
                {
                    var rawTitle = property.Name;
                    if (property.Value.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                    {
                        rawTitle = titleProp.GetString() ?? property.Name;
                    }

                    string? rawDescription = null;
                    if (property.Value.TryGetProperty("description", out var descriptionProp) && descriptionProp.ValueKind == JsonValueKind.String)
                    {
                        rawDescription = descriptionProp.GetString();
                    }

                    var field = new DynamicConfigField
                    {
                        Key = property.Name,
                        Title = _localization.TranslateSchemaFieldTitle(property.Name, rawTitle),
                        Description = _localization.TranslateSchemaFieldDescription(property.Name, rawDescription)
                    };

                    if (property.Value.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                    {
                        field.Type = typeProp.GetString() ?? "string";
                    }

                    if (property.Value.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var option in enumProp.EnumerateArray())
                        {
                            var value = option.ValueKind switch
                            {
                                JsonValueKind.Number => option.GetRawText(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.String => option.GetString() ?? string.Empty,
                                _ => option.ToString()
                            };
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                field.Options.Add(new DynamicConfigOption
                                {
                                    Value = value,
                                    Label = value
                                });
                            }
                        }
                    }

                    result.Add(field);
                }

                return result;
            }
            catch
            {
                return [];
            }
        }

        public string BuildConfigurationJson(
            List<DynamicConfigField> fields,
            Dictionary<string, string> values,
            IReadOnlyDictionary<string, string>? customParameters = null)
        {
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                values.TryGetValue(field.Key, out var raw);
                if (field.IsBoolean)
                {
                    payload[field.Key] = bool.TryParse(raw, out var boolValue) && boolValue;
                    continue;
                }

                if (field.IsInteger)
                {
                    if (int.TryParse(raw, out var intValue))
                    {
                        payload[field.Key] = intValue;
                    }
                    else
                    {
                        payload[field.Key] = 0;
                    }
                    continue;
                }

                payload[field.Key] = raw ?? string.Empty;
            }

            if (customParameters != null)
            {
                var customPayload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in customParameters)
                {
                    var key = item.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    customPayload[key] = item.Value?.Trim() ?? string.Empty;
                }

                if (customPayload.Count > 0)
                {
                    payload["customParameters"] = customPayload;
                }
            }

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }

        public Dictionary<string, string> ParseConfigJsonToDictionary(string? json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        continue;
                    }

                    result[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => string.Empty,
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch
            {
                // ignore
            }

            return result;
        }

        public Dictionary<string, string> ParseCustomParameters(string? json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("customParameters", out var custom) ||
                    custom.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var property in custom.EnumerateObject())
                {
                    var key = property.Name.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    result[key] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => string.Empty,
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch
            {
                // ignore
            }

            return result;
        }

        public string BuildDefaultGameConfiguration(long gameDefinitionId, List<GameDefinitionDto> gameDefinitions)
        {
            var key = gameDefinitions.FirstOrDefault(x => x.Id == gameDefinitionId)?.Key;
            return key switch
            {
                "minecraft-java" => "{\n  \"serverVersion\": \"\",\n  \"onlineMode\": true,\n  \"motd\": \"Playmox Minecraft\",\n  \"maxPlayers\": 20,\n  \"difficulty\": \"easy\",\n  \"gameMode\": \"survival\",\n  \"pvp\": true,\n  \"allowFlight\": false,\n  \"viewDistance\": 10,\n  \"simulationDistance\": 10,\n  \"spawnProtection\": 16,\n  \"enableCommandBlock\": false,\n  \"whiteList\": false,\n  \"enforceWhitelist\": false,\n  \"allowNether\": true,\n  \"hardcore\": false\n}",
                "terraria" => "{\n  \"worldName\": \"PlaymoxWorld\",\n  \"worldSeed\": \"\",\n  \"autoCreateSize\": 2,\n  \"maxPlayers\": 8,\n  \"password\": \"\",\n  \"motd\": \"Playmox Terraria\"\n}",
                _ => "{}"
            };
        }
    }

    public sealed class DynamicConfigField
    {
        public string Key { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Type { get; set; } = "string";
        public List<DynamicConfigOption> Options { get; set; } = [];
        public bool IsBoolean => string.Equals(Type, "boolean", StringComparison.OrdinalIgnoreCase);
        public bool IsInteger => string.Equals(Type, "integer", StringComparison.OrdinalIgnoreCase);
        public bool IsPassword => Key.Contains("password", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DynamicConfigOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
