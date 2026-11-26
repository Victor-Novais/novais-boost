using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;

namespace NovaisBoost.Core;

public static class TweakCatalog
{
    private const string ResourcePrefix = "Tweaks.";
    private const string SchemaResource = ResourcePrefix + "tweak.schema.json";
    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.Build(JsonElement.Parse(ReadResource(SchemaResource))));

    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static IReadOnlyList<TweakDefinition> LoadEmbedded()
    {
        var names = typeof(TweakCatalog).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name != SchemaResource)
            .Order(StringComparer.Ordinal);
        var tweaks = names.Select(name => Parse(ReadResource(name), name[ResourcePrefix.Length..])).ToArray();
        if (tweaks.Select(tweak => tweak.Id).Distinct(StringComparer.Ordinal).Count() != tweaks.Length)
            throw new InvalidDataException("O catálogo contém identificadores de ajuste repetidos.");
        return tweaks;
    }

    public static TweakDefinition Parse(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            var result = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!result.IsValid)
            {
                var locations = (result.Details ?? []).Where(detail => !detail.IsValid && detail.Errors?.Count > 0)
                    .Select(detail => detail.InstanceLocation.ToString()).Where(path => path.Length > 0)
                    .Distinct().Take(5);
                var fields = string.Join(", ", locations);
                throw new InvalidDataException(fields.Length == 0
                    ? "campos obrigatórios ausentes ou propriedades desconhecidas."
                    : $"confira os campos {fields} de acordo com o schema.");
            }

            var tweak = document.RootElement.Deserialize<TweakDefinition>(JsonOptions)!;
            if (tweak.Actions.Select(action => action.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tweak.Actions.Length)
                throw new InvalidDataException("o mesmo alvo aparece mais de uma vez.");
            foreach (var action in tweak.Actions.OfType<RegistryAction>())
            {
                action.Expected?.Validate();
                foreach (var accepted in action.Accept ?? [])
                    accepted.Validate();
            }
            return tweak;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"JSON inválido em {source} (linha {(error.LineNumber ?? 0) + 1}, campo {error.Path ?? "$"}).", error);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException($"Ajuste inválido em {source}. {error.Message}", error);
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(TweakCatalog).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Recurso de catálogo ausente: {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
