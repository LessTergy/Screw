using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Screw.Levels;
using UnityEngine;

namespace Screw.Editor.Levels
{
    public sealed class LevelRouteData
    {
        public const string LevelOnePath = "Assets/Content/Tests/Fixtures/LevelRoutes/Level_01.json";

        [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion;
        [JsonProperty("levelNumber", Required = Required.Always)] public int LevelNumber;
        [JsonProperty("camera", Required = Required.Always)] public RouteCameraData Camera;
        [JsonProperty("steps", Required = Required.Always)] public RouteStepData[] Steps;

        public static LevelRouteData Read(string json, LevelSourceData source)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Route JSON must not be empty.");
            }

            try
            {
                var token = JObject.Parse(json, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
                var route = token.ToObject<LevelRouteData>(JsonSerializer.Create(new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore
                }));
                route.Validate(source);
                return route;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Invalid route JSON.", exception);
            }
        }

        public void Validate(LevelSourceData source)
        {
            LevelDataValidator.ValidateLevel(source);
            Require(SchemaVersion == 1 && LevelNumber == source.Number, "Route schema or level does not match.");
            Require(Camera != null && Finite(Camera.FieldOfViewDegrees)
                && Camera.FieldOfViewDegrees >= 1f && Camera.FieldOfViewDegrees < 179f
                && Finite(Camera.Aspect) && Camera.Aspect > 0f, "Invalid route projection.");
            Require(Steps != null && Steps.All(step => step != null)
                && Steps.Length == source.Bolts.Length, "Route must contain every source bolt.");
            var ids = Steps.Select(step => step.BoltId).ToArray();
            Require(ids.Distinct().Count() == ids.Length
                && ids.OrderBy(id => id).SequenceEqual(source.Bolts.Select(bolt => bolt.Id).OrderBy(id => id)),
                "Unknown, repeated or missing route bolt.");
            foreach (var step in Steps)
            {
                Require(VectorValid(step.CameraPosition, 3) && VectorValid(step.CameraEulerDegrees, 3), "Invalid route pose.");
                Require(VectorValid(step.ViewportPoint, 2) && step.ViewportPoint.All(value => value >= 0f && value <= 1f),
                    "Invalid route viewport point.");
            }
        }

        public static Vector3 Vector(float[] values)
        {
            return new Vector3(values[0], values[1], values[2]);
        }

        private static bool VectorValid(float[] values, int length)
        {
            return values != null && values.Length == length && values.All(Finite);
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }

    public sealed class RouteCameraData
    {
        [JsonProperty("fieldOfViewDegrees", Required = Required.Always)] public float FieldOfViewDegrees;
        [JsonProperty("aspect", Required = Required.Always)] public float Aspect;
    }

    public sealed class RouteStepData
    {
        [JsonProperty("boltId", Required = Required.Always)] public int BoltId;
        [JsonProperty("cameraPosition", Required = Required.Always)] public float[] CameraPosition;
        [JsonProperty("cameraEulerDegrees", Required = Required.Always)] public float[] CameraEulerDegrees;
        [JsonProperty("viewportPoint", Required = Required.Always)] public float[] ViewportPoint;
    }
}
