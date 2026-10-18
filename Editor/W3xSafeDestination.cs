using System;
using System.IO;

namespace MusafirEx.CncSage.Editor
{
    /// <summary>
    /// Safely commits a previously serialized/validated W3X staging file.
    /// A Windows file lock or read-only imported output cannot silently
    /// discard the validated export or damage the existing destination.
    /// </summary>
    internal static class W3xSafeDestination
    {
        internal static string Commit(string stagingPath, string destination,
            out bool savedAsCopy)
        {
            return Commit(stagingPath, destination, out savedAsCopy, null);
        }

        // Injectable replacement action is only used by editor regression
        // tests to simulate deterministic Windows sharing violations.
        internal static string Commit(string stagingPath, string destination,
            out bool savedAsCopy, Action<string, string> replaceExisting)
        {
            savedAsCopy = false;
            if (string.IsNullOrWhiteSpace(stagingPath) ||
                string.IsNullOrWhiteSpace(destination) ||
                !File.Exists(stagingPath))
                throw new IOException(
                    "Validated W3X staging file does not exist: " + stagingPath);

            stagingPath = Path.GetFullPath(stagingPath);
            destination = Path.GetFullPath(destination);
            if (string.Equals(stagingPath, destination,
                StringComparison.OrdinalIgnoreCase))
                throw new IOException(
                    "Staging path must differ from destination.");

            if (!File.Exists(destination))
            {
                File.Move(stagingPath, destination);
                return destination;
            }

            try
            {
                if (replaceExisting == null)
                    File.Replace(stagingPath, destination, null);
                else
                    replaceExisting(stagingPath, destination);
                if (File.Exists(stagingPath))
                    throw new IOException(
                        "Replace operation left the staging file present.");
                return destination;
            }
            catch (IOException)
            {
                // A locked target commonly produces "Unable to remove the
                // file to be replaced". Keep the old file and export as a
                // separate sibling .w3x rather than deleting the new data.
                return SaveDifferentName(stagingPath, destination,
                    out savedAsCopy);
            }
            catch (UnauthorizedAccessException)
            {
                return SaveDifferentName(stagingPath, destination,
                    out savedAsCopy);
            }
        }

        private static string SaveDifferentName(
            string stagingPath, string destination, out bool savedAsCopy)
        {
            savedAsCopy = false;
            if (!File.Exists(stagingPath))
                throw new IOException(
                    "Original destination replacement failed and the " +
                    "validated staging file is no longer available. " +
                    "Inspect the destination before retrying: " +
                    destination);
            // Fallback is safe only when the original destination
            // still exists. If it disappeared, fail without guessing.
            if (!File.Exists(destination))
                throw new IOException(
                    "Destination disappeared during replacement. " +
                    "Validated staging file retained at: " + stagingPath);

            string directory = Path.GetDirectoryName(destination);
            string baseName = Path.GetFileNameWithoutExtension(destination);
            string extension = Path.GetExtension(destination);
            for (int i = 1; i <= 999; i++)
            {
                string output = Path.Combine(directory,
                    baseName + "_copy_" + i.ToString("D3") + extension);
                if (File.Exists(output))
                    continue;
                try
                {
                    File.Move(stagingPath, output);
                    savedAsCopy = true;
                    return output;
                }
                catch (IOException)
                {
                    // Retry only a collision from another process. If
                    // writing to the folder is forbidden, propagate the
                    // error and preserve the validated staging file.
                    if (!File.Exists(output) || !File.Exists(stagingPath))
                        throw;
                }
            }
            throw new IOException(
                "Unable to find a free sibling filename for " +
                destination + " (001-999). " +
                "Validated staging file retained at: " + stagingPath);
        }
    }
}
