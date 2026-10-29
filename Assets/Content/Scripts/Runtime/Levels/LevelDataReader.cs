using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Screw.Levels
{
    public static class LevelDataReader
    {
        public const int SupportedSchemaVersion = 1;

        public static LevelSourceCatalogData ReadCatalog(string json)
        {
            var catalog = Read<LevelSourceCatalogData>(json);
            RequireVersion(catalog.SchemaVersion);
            LevelDataValidator.ValidateCatalog(catalog);
            return catalog;
        }

        public static LevelSourceData ReadLevel(string json, int schemaVersion)
        {
            RequireVersion(schemaVersion);
            var level = Read<LevelSourceData>(json);
            LevelDataValidator.ValidateLevel(level);
            return level;
        }

        private static T Read<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Level JSON must not be empty.");
            }

            try
            {
                var token = JObject.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                var serializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore
                });
                return token.ToObject<T>(serializer);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Invalid level JSON: " + exception.Message, exception);
            }
        }

        private static void RequireVersion(int version)
        {
            if (version != SupportedSchemaVersion)
            {
                throw new InvalidDataException("Unsupported level schema version: " + version + ".");
            }
        }
    }
}
