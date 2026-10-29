using Newtonsoft.Json;

namespace Screw.Levels
{
    public sealed class LevelSourceCatalogData
    {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion;

        [JsonProperty("partCount", Required = Required.Always)]
        public int PartCount;

        [JsonProperty("boltCount", Required = Required.Always)]
        public int BoltCount;

        [JsonProperty("boltDefaultPaletteIndex", Required = Required.Always)]
        public int BoltDefaultPaletteIndex;

        [JsonProperty("sourceCommit", Required = Required.Always)]
        public string SourceCommit;

        [JsonProperty("sourceUnityVersion", Required = Required.Always)]
        public string SourceUnityVersion;

        [JsonProperty("unityRenderPipeline", Required = Required.Always)]
        public string UnityRenderPipeline;

        [JsonProperty("coordinateSystem", Required = Required.Always)]
        public string CoordinateSystem;

        [JsonProperty("boltColors", Required = Required.Always)]
        public string BoltColors;

        [JsonProperty("rootScreenRatio", Required = Required.Always)]
        public double RootScreenRatio;

        [JsonProperty("levels", Required = Required.Always)]
        public LevelSourceData[] Levels;

        [JsonProperty("boltPalette", Required = Required.Always)]
        public BoltPaletteData[] BoltPalette;
    }

    public sealed class LevelSourceData
    {
        [JsonProperty("number", Required = Required.Always)]
        public int Number;

        [JsonProperty("sourceCampaignIndex", Required = Required.Always)]
        public int SourceCampaignIndex;

        [JsonProperty("sourceModelId", Required = Required.Always)]
        public int SourceModelId;

        [JsonProperty("sourceName", Required = Required.Always)]
        public string SourceName;

        [JsonProperty("modelPosition", Required = Required.Always)]
        public double[] ModelPosition;

        [JsonProperty("modelEulerDegrees", Required = Required.Always)]
        public double[] ModelEulerDegrees;

        [JsonProperty("rootEulerDegrees", Required = Required.Always)]
        public double[] RootEulerDegrees;

        [JsonProperty("modelScale", Required = Required.Always)]
        public double ModelScale;

        [JsonProperty("rootScale", Required = Required.Always)]
        public double RootScale;

        [JsonProperty("allowedBoltPaletteIndices", Required = Required.Always)]
        public int[] AllowedBoltPaletteIndices;

        [JsonProperty("parts", Required = Required.Always)]
        public PartSourceData[] Parts;

        [JsonProperty("bolts", Required = Required.Always)]
        public BoltSourceData[] Bolts;

        [JsonProperty("sourcePrefab", Required = Required.Always)]
        public string SourcePrefab;

        [JsonProperty("prefab", Required = Required.Always)]
        public string Prefab;

        [JsonProperty("glb", Required = Required.Always)]
        public string Glb;
    }

    public sealed class PartSourceData
    {
        [JsonProperty("id", Required = Required.Always)]
        public int Id;

        [JsonProperty("childIndex", Required = Required.Always)]
        public int ChildIndex;

        [JsonProperty("sourceLayer", Required = Required.Always)]
        public int SourceLayer;

        [JsonProperty("boltIds", Required = Required.Always)]
        public int[] BoltIds;

        [JsonProperty("sourceReleaseForce", Required = Required.Always)]
        public double[] SourceReleaseForce;

        [JsonProperty("sourceSpin", Required = Required.Always)]
        public bool SourceSpin;

        [JsonProperty("name", Required = Required.Always)]
        public string Name;

        [JsonProperty("nodePath", Required = Required.Always)]
        public string NodePath;
    }

    public sealed class BoltSourceData
    {
        [JsonProperty("id", Required = Required.Always)]
        public int Id;

        [JsonProperty("ownerId", Required = Required.Always)]
        public int OwnerId;

        [JsonProperty("position", Required = Required.Always)]
        public double[] Position;

        [JsonProperty("eulerDegrees", Required = Required.Always)]
        public double[] EulerDegrees;

        [JsonProperty("sourceWhitelist", Required = Required.Always)]
        public string SourceWhitelist;

        [JsonProperty("nodePath", Required = Required.Always)]
        public string NodePath;
    }

    public sealed class BoltPaletteData
    {
        [JsonProperty("index", Required = Required.Always)]
        public int Index;

        [JsonProperty("primary", Required = Required.Always)]
        public string Primary;

        [JsonProperty("secondary", Required = Required.Always)]
        public string Secondary;

        [JsonProperty("prefab", Required = Required.Always)]
        public string Prefab;

        [JsonProperty("glb", Required = Required.Always)]
        public string Glb;

        [JsonProperty("primarySrgb", Required = Required.Always)]
        public double[] PrimarySrgb;

        [JsonProperty("secondarySrgb", Required = Required.Always)]
        public double[] SecondarySrgb;
    }
}
