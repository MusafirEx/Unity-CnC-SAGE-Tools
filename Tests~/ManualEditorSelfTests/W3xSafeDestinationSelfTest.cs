using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// File-IO-only smoke test. Simulates a Windows sharing violation
    /// deterministically; does not modify any Project assets or source W3X.
    /// </summary>
    internal static class W3xSafeDestinationSelfTest
    {
        [MenuItem("Tools/C&C SAGE/Run Phase 2D Export File Safety Self-Test")]
        private static void Run()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "CncSageExportTest_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                string destination = Path.Combine(folder, "Pitbull_unity_skin.w3x");
                string staging = destination + ".sage-export-tmp";

                // First export: file is absent, so the result must take
                // the selected filename directly.
                File.WriteAllText(staging, "<AssetDeclaration id='A'/>");
                bool savedAsCopy;
                string path = W3xSafeDestination.Commit(
                    staging, destination, out savedAsCopy);
                Assert(path == destination && !savedAsCopy &&
                    File.ReadAllText(destination).Contains("id='A'") &&
                    !File.Exists(staging),
                    "First export was not committed to the exact file.");

                // Simulate File.Replace throwing on a busy Windows file.
                // The prior export must survive byte-for-byte.
                File.WriteAllText(staging, "<AssetDeclaration id='B'/>");
                path = W3xSafeDestination.Commit(staging, destination,
                    out savedAsCopy, (source, target) =>
                    {
                        throw new IOException(
                            "Unable to remove the file to be replaced");
                    });
                Assert(savedAsCopy &&
                    Path.GetFileName(path) == "Pitbull_unity_skin_copy_001.w3x" &&
                    File.ReadAllText(destination).Contains("id='A'") &&
                    File.ReadAllText(path).Contains("id='B'") &&
                    !File.Exists(staging),
                    "Locked destination fallback damaged original or new data.");

                // An existing fallback filename must never be overwritten.
                File.WriteAllText(staging, "<AssetDeclaration id='C'/>");
                string second = W3xSafeDestination.Commit(staging, destination,
                    out savedAsCopy, (source, target) =>
                    {
                        throw new UnauthorizedAccessException("read-only");
                    });
                Assert(savedAsCopy &&
                    Path.GetFileName(second) ==
                        "Pitbull_unity_skin_copy_002.w3x" &&
                    File.ReadAllText(path).Contains("id='B'") &&
                    File.ReadAllText(second).Contains("id='C'") &&
                    File.ReadAllText(destination).Contains("id='A'"),
                    "Multiple fallback copies must be unique and preserve data.");

                Debug.Log(
                    "[C&C SAGE Phase 2D] Export file safety self-test PASS: " +
                    "fresh output, locked replacement fallback, " +
                    "unique sibling copy, no overwritten source.");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[C&C SAGE Phase 2D] Export file safety self-test FAILED: " +
                    ex);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }
                catch (IOException ex)
                {
                    Debug.LogWarning(
                        "[C&C SAGE] Temporary file safety test cleanup " +
                        "could not delete " + folder + ": " + ex.Message);
                }
            }
        }

        private static void Assert(bool valid, string message)
        {
            if (!valid)
                throw new InvalidDataException(message);
        }
    }
}
