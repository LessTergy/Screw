using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Screw.Editor.Levels;

namespace Screw.Tests.Levels
{
    public sealed class GeneratedLevelImportTests
    {
        [TestCase(false, "checksum")]
        [TestCase(true, "URI references")]
        public void RejectsUntrustedGlbBeforeLoadingUnityAssets(bool matchingChecksum, string expectedMessage)
        {
            var folder = Path.Combine(Path.GetTempPath(), "ScrewGeneratedLevel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var glb = Path.Combine(folder, "Model.glb");
            var manifest = Path.Combine(folder, "ModelManifest.json");
            try
            {
                const string document = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"external.bin\",\"byteLength\":4}]}";
                var json = Encoding.UTF8.GetBytes(document.PadRight((document.Length + 3) / 4 * 4));
                using (var writer = new BinaryWriter(File.Create(glb)))
                {
                    writer.Write(0x46546C67u);
                    writer.Write(2u);
                    writer.Write((uint)(20 + json.Length));
                    writer.Write((uint)json.Length);
                    writer.Write(0x4E4F534Au);
                    writer.Write(json);
                }

                using (var sha = SHA256.Create())
                {
                    File.WriteAllText(manifest, new JObject
                    {
                        ["schemaVersion"] = 1,
                        ["modelName"] = "TestModel",
                        ["transformCoordinates"] = "Blender right-handed, Z up; quaternion WXYZ",
                        ["glbSha256"] = matchingChecksum ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(glb))).Replace("-", "") : "wrong",
                        ["parts"] = new JArray(),
                        ["bolts"] = new JArray()
                    }.ToString());
                }

                var error = Assert.Throws<InvalidDataException>(() => GeneratedLevelImportUtility.Import(folder));
                StringAssert.Contains(expectedMessage, error.Message);
            }
            finally
            {
                File.Delete(manifest);
                File.Delete(glb);
                Directory.Delete(folder);
            }
        }
    }
}
