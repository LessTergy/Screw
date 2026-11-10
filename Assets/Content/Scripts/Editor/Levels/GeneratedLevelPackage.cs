using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Screw.Levels;

namespace Screw.Editor.Levels
{
    internal sealed class GeneratedLevelPackage
    {
        private const int MaxGlbBytes = 64 * 1024 * 1024;
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const string BlenderCoordinates = "Blender right-handed, Z up; quaternion WXYZ";

        private GeneratedLevelPackage(byte[] glb, Manifest manifest, string manifestHash)
        {
            Glb = glb;
            Metadata = manifest;
            ManifestHash = manifestHash;
            ImportGlbHash = Hash(glb);
        }

        public byte[] Glb { get; }
        public Manifest Metadata { get; }
        public string ManifestHash { get; }
        public string ImportGlbHash { get; }

        public static GeneratedLevelPackage Read(string folder)
        {
            var manifestFile = Path.Combine(folder, "ModelManifest.json");
            var glbFiles = Directory.GetFiles(folder, "*.glb");
            if (!File.Exists(manifestFile) || glbFiles.Length != 1)
            {
                throw new InvalidDataException("Choose a folder with ModelManifest.json and exactly one GLB.");
            }

            var manifestBytes = File.ReadAllBytes(manifestFile);
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore
            });
            var manifest = Parse(Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF')).ToObject<Manifest>(serializer);
            if (manifest.Version != 1 || !Regex.IsMatch(manifest.ModelName, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,63}\z"))
            {
                throw new InvalidDataException("Invalid manifest schemaVersion or modelName.");
            }

            var glbSize = new FileInfo(glbFiles[0]).Length;
            if (glbSize < 20 || glbSize > MaxGlbBytes)
            {
                throw new InvalidDataException("The GLB must be a valid file of at most 64 MiB.");
            }

            var glb = File.ReadAllBytes(glbFiles[0]);
            var hash = Hash(glb);
            if (!string.Equals(manifest.GlbSha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The GLB checksum differs from ModelManifest.json.");
            }

            var reportPath = Path.Combine(folder, "MeshValidationReport.json");
            if (File.Exists(reportPath) &&
                !string.Equals((string)Parse(File.ReadAllText(reportPath))["glbSha256"], hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("MeshValidationReport.json describes a different GLB.");
            }

            var importGlb = PrepareEmbeddedGlb(glb);
            if (manifest.TransformCoordinates != BlenderCoordinates)
            {
                throw new InvalidDataException("The manifest must describe Blender Z-up marker transforms.");
            }

            if (manifest.Parts.Any(part => part == null || part.TriangleCount < 4)
                || manifest.Bolts.Any(bolt => bolt == null || bolt.OutwardAxis != "+Z"))
            {
                throw new InvalidDataException("Parts need triangle counts and every bolt marker needs outwardAxis +Z.");
            }

            return new GeneratedLevelPackage(importGlb, manifest, Hash(manifestBytes));
        }

        public LevelSourceData CreateSource(int number, int[] palette, string assetFolder)
        {
            var source = new LevelSourceData
            {
                Number = number,
                SourceCampaignIndex = number - 1,
                SourceModelId = Convert.ToInt32(ManifestHash.Substring(0, 7), 16) + 1,
                SourceName = Metadata.ModelName,
                SourcePrefab = assetFolder + "/Model.prefab",
                Prefab = assetFolder + "/Level.prefab",
                Glb = assetFolder + "/Model.glb",
                ModelPosition = new double[3],
                ModelEulerDegrees = new double[3],
                ModelScale = 1,
                RootEulerDegrees = new double[3],
                RootScale = 1,
                AllowedBoltPaletteIndices = palette,
                Parts = Metadata.Parts.OrderBy(part => part.Id).Select((part, index) => new PartSourceData
                {
                    Id = part.Id,
                    Name = part.Name,
                    NodePath = part.NodePath,
                    ChildIndex = index,
                    BoltIds = part.BoltIds,
                    SourceReleaseForce = new double[3]
                }).ToArray(),
                Bolts = Metadata.Bolts.OrderBy(bolt => bolt.Id).Select(bolt => new BoltSourceData
                {
                    Id = bolt.Id,
                    OwnerId = bolt.OwnerId,
                    NodePath = bolt.NodePath,
                    Position = new double[3],
                    EulerDegrees = new double[3],
                    SourceWhitelist = string.Empty
                }).ToArray()
            };
            LevelDataValidator.ValidateLevel(source);
            return source;
        }

        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static JObject Parse(string json)
        {
            return JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }

        private static byte[] PrepareEmbeddedGlb(byte[] bytes)
        {
            using (var reader = new BinaryReader(new MemoryStream(bytes, writable: false)))
            {
                if (reader.ReadUInt32() != GlbMagic || reader.ReadUInt32() != 2 || reader.ReadUInt32() != bytes.Length)
                {
                    throw new InvalidDataException("Invalid GLB 2.0 header.");
                }

                var jsonLength = reader.ReadUInt32();
                if (reader.ReadUInt32() != JsonChunk || jsonLength > bytes.Length - 20 || jsonLength % 4 != 0)
                {
                    throw new InvalidDataException("Invalid GLB JSON chunk.");
                }

                var gltf = Parse(Encoding.UTF8.GetString(reader.ReadBytes((int)jsonLength)));
                if (gltf.Descendants().OfType<JProperty>().Any(property => property.Name == "uri"))
                {
                    throw new InvalidDataException("The GLB must embed its buffers and images without URI references.");
                }

                if (!(gltf["scenes"] is JArray scenes))
                {
                    throw new InvalidDataException("The GLB has no scenes.");
                }

                var populated = scenes.Where(scene => scene["nodes"] is JArray roots && roots.Count > 0).ToArray();
                if (populated.Length != 1 || ((int?)gltf["scene"] ?? scenes.IndexOf(populated[0])) != scenes.IndexOf(populated[0]))
                {
                    throw new InvalidDataException("The GLB must have one populated scene selected as the default.");
                }

                if (scenes.Count == 1)
                {
                    return bytes;
                }

                // glTFast 6.14 tries to instantiate empty Blender scenes and fails on missing root nodes.
                gltf["scenes"] = new JArray(populated[0].DeepClone());
                gltf["scene"] = 0;
                var json = Encoding.UTF8.GetBytes(gltf.ToString(Formatting.None));
                var paddedLength = (json.Length + 3) / 4 * 4;
                var tail = (int)reader.BaseStream.Position;
                using (var output = new MemoryStream())
                using (var writer = new BinaryWriter(output))
                {
                    writer.Write(GlbMagic);
                    writer.Write(2u);
                    writer.Write(20 + paddedLength + bytes.Length - tail);
                    writer.Write(paddedLength);
                    writer.Write(JsonChunk);
                    writer.Write(json);
                    for (var i = json.Length; i < paddedLength; i++)
                    {
                        writer.Write((byte)' ');
                    }

                    writer.Write(bytes, tail, bytes.Length - tail);
                    return output.ToArray();
                }
            }
        }

        internal sealed class Manifest
        {
            [JsonProperty("schemaVersion", Required = Required.Always)] public int Version;
            [JsonProperty("modelName", Required = Required.Always)] public string ModelName;
            [JsonProperty("glbSha256", Required = Required.Always)] public string GlbSha256;
            [JsonProperty("transformCoordinates", Required = Required.Always)] public string TransformCoordinates;
            [JsonProperty("parts", Required = Required.Always)] public Part[] Parts;
            [JsonProperty("bolts", Required = Required.Always)] public Bolt[] Bolts;
        }

        internal sealed class Part
        {
            [JsonProperty("id", Required = Required.Always)] public int Id;
            [JsonProperty("name", Required = Required.Always)] public string Name;
            [JsonProperty("nodePath", Required = Required.Always)] public string NodePath;
            [JsonProperty("triangleCount", Required = Required.Always)] public int TriangleCount;
            [JsonProperty("boltIds", Required = Required.Always)] public int[] BoltIds;
        }

        internal sealed class Bolt
        {
            [JsonProperty("id", Required = Required.Always)] public int Id;
            [JsonProperty("ownerId", Required = Required.Always)] public int OwnerId;
            [JsonProperty("nodePath", Required = Required.Always)] public string NodePath;
            [JsonProperty("outwardAxis", Required = Required.Always)] public string OutwardAxis;
        }
    }
}
