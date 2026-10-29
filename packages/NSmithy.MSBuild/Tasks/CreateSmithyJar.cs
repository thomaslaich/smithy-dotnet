using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>
/// Packs .smithy model files into a Maven-compatible JAR artifact (plus POM and checksums),
/// mirroring the structure expected by Smithy's JAR model discovery.
/// </summary>
public sealed class CreateSmithyJar : MsBuildTask
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

    /// <summary>
    /// The contracts project's smithy-build.json. Its <c>maven.dependencies</c> become POM
    /// dependencies, so a consumer resolving the JAR also gets the traits the model uses. Empty
    /// when there is none.
    /// </summary>
    public string BuildFile { get; set; } = "";

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

        if (!TryReadDependencies(out var dependencies))
        {
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

        Log.LogMessage(
            MessageImportance.High,
            $"NSmithy: packing {smithyFiles.Count} .smithy file(s) → {Path.GetFileName(jarPath)}"
        );

        using (var jarStream = File.Create(jarPath))
        using (var zip = new ZipArchive(jarStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            WriteEntry(zip, "META-INF/smithy/manifest", Encoding.UTF8.GetBytes(manifest));
            foreach (var (file, entry) in smithyFiles.Zip(entries))
                WriteEntry(zip, $"META-INF/smithy/{entry}", File.ReadAllBytes(file));
        }

        var pom = GeneratePom(dependencies);
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

    private bool TryReadDependencies(out List<string[]> dependencies)
    {
        dependencies = [];
        if (string.IsNullOrEmpty(BuildFile) || !File.Exists(BuildFile))
        {
            return true;
        }

        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        if (
            JsonNode.Parse(File.ReadAllText(BuildFile), documentOptions: options)
                is not JsonObject buildFile
            || buildFile["maven"]?["dependencies"] is not JsonArray coordinates
        )
        {
            return true;
        }

        foreach (var coordinate in coordinates.Select(node => node?.GetValue<string>() ?? ""))
        {
            // smithy-build.json takes groupId:artifactId:version, as the Smithy CLI does.
            var parts = coordinate.Split(':');
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            {
                Log.LogError(
                    $"NSmithy: Maven dependency '{coordinate}' in {BuildFile} is not a groupId:artifactId:version coordinate."
                );
                return false;
            }

            dependencies.Add(parts);
        }

        return true;
    }

    private string GeneratePom(List<string[]> dependencies)
    {
        XNamespace pom = "http://maven.apache.org/POM/4.0.0";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var project = new XElement(
            pom + "project",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(
                xsi + "schemaLocation",
                "http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd"
            ),
            new XElement(pom + "modelVersion", "4.0.0"),
            new XElement(pom + "groupId", GroupId),
            new XElement(pom + "artifactId", ArtifactId),
            new XElement(pom + "version", Version),
            new XElement(pom + "packaging", "jar")
        );
        if (dependencies.Count > 0)
        {
            project.Add(
                new XElement(
                    pom + "dependencies",
                    dependencies.Select(parts => new XElement(
                        pom + "dependency",
                        new XElement(pom + "groupId", parts[0]),
                        new XElement(pom + "artifactId", parts[1]),
                        new XElement(pom + "version", parts[2])
                    ))
                )
            );
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), project).ToString();
    }

    // MD5 and SHA1 are Maven's required checksum algorithms — not used for security.
#pragma warning disable CA5351, CA5350
    private static void WriteChecksums(string filePath)
    {
        var content = File.ReadAllBytes(filePath);
        File.WriteAllText(
            filePath + ".md5",
            Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant()
        );
        File.WriteAllText(
            filePath + ".sha1",
            Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant()
        );
    }
#pragma warning restore CA5351, CA5350
}
