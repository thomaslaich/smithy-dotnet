using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Packs .smithy model files into a Maven-compatible JAR artifact (plus POM and checksums),
/// mirroring the structure expected by Smithy's JAR model discovery.
/// </summary>
public sealed class CreateSmithyJarTask : MsBuildTask
{
    /// <summary>The .smithy files to include in the JAR.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>
    /// Root directory used to compute relative paths inside the JAR manifest.
    /// Typically $(SmithySources) (e.g. the model/ folder of the contracts project).
    /// </summary>
    [Required]
    public string SourcesRoot { get; set; } = "";

    /// <summary>Maven groupId (e.g. io.github.acme).</summary>
    [Required]
    public string GroupId { get; set; } = "";

    /// <summary>Maven artifactId (e.g. my-contracts).</summary>
    [Required]
    public string ArtifactId { get; set; } = "";

    /// <summary>Maven version (e.g. 1.0.0 or 1.0.0-SNAPSHOT).</summary>
    [Required]
    public string Version { get; set; } = "";

    /// <summary>Directory where the JAR, POM, and checksum files are written.</summary>
    [Required]
    public string OutputDirectory { get; set; } = "";

    /// <summary>Full path of the JAR file that was created.</summary>
    [Output]
    public string JarFile { get; set; } = "";

    public override bool Execute()
    {
        var smithyFiles = Sources
            .Select(item => item.GetMetadata("FullPath"))
            .Where(p => !string.IsNullOrEmpty(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        if (smithyFiles.Count == 0)
        {
            Log.LogError("NSmithy: no .smithy files found to pack into JAR.");
            return false;
        }

        Directory.CreateDirectory(OutputDirectory);

        var baseName = $"{ArtifactId}-{Version}";
        var jarPath = Path.Combine(OutputDirectory, $"{baseName}.jar");
        var pomPath = Path.Combine(OutputDirectory, $"{baseName}.pom");

        var sourcesRoot = Path.GetFullPath(SourcesRoot);
        var entries = smithyFiles
            .Select(f => Path.GetRelativePath(sourcesRoot, f).Replace('\\', '/'))
            .ToList();

        var manifest = string.Join("\n", entries) + "\n";

        Log.LogMessage(MessageImportance.High,
            $"NSmithy: packing {smithyFiles.Count} .smithy file(s) → {Path.GetFileName(jarPath)}");

        using (var jarStream = File.Create(jarPath))
        using (var zip = new ZipArchive(jarStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            WriteEntry(zip, "META-INF/smithy/manifest", Encoding.UTF8.GetBytes(manifest));
            foreach (var (file, entry) in smithyFiles.Zip(entries))
                WriteEntry(zip, $"META-INF/smithy/{entry}", File.ReadAllBytes(file));
        }

        var pom = GeneratePom();
        File.WriteAllText(pomPath, pom, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        WriteChecksums(jarPath);
        WriteChecksums(pomPath);

        JarFile = jarPath;
        return true;
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private string GeneratePom() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                XName.Get("project", "http://maven.apache.org/POM/4.0.0"),
                new XAttribute(
                    XName.Get("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance"),
                    "http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd"
                ),
                new XElement("modelVersion", "4.0.0"),
                new XElement("groupId", GroupId),
                new XElement("artifactId", ArtifactId),
                new XElement("version", Version),
                new XElement("packaging", "jar")
            )
        ).ToString();

    // MD5 and SHA1 are Maven's required checksum algorithms — not used for security.
#pragma warning disable CA5351, CA5350
    private static void WriteChecksums(string filePath)
    {
        var content = File.ReadAllBytes(filePath);
        File.WriteAllText(filePath + ".md5", Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant());
        File.WriteAllText(filePath + ".sha1", Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant());
    }
#pragma warning restore CA5351, CA5350
}
