using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DotnetNsmithy.Commands;

internal static class PackCommand
{
    public static Command Create()
    {
        var projectOption = new Option<FileInfo?>(
            aliases: ["--project", "-p"],
            description: "Path to the .csproj file. Auto-discovered in the current directory when omitted."
        );

        var sourcesOption = new Option<DirectoryInfo?>(
            aliases: ["--sources", "-s"],
            description: "Directory containing .smithy source files. Overrides SmithySources in the .csproj."
        );

        var groupOption = new Option<string?>(
            aliases: ["--group", "-g"],
            description: "Maven groupId (e.g. com.example). Overrides SmithyMavenGroupId in the .csproj."
        );

        var artifactOption = new Option<string?>(
            aliases: ["--artifact", "-a"],
            description: "Maven artifactId (e.g. my-contracts). Overrides SmithyMavenArtifactId in the .csproj."
        );

        var versionOption = new Option<string?>(
            aliases: ["--version", "-v"],
            description: "Maven version (e.g. 1.0.0). Overrides the version in the .csproj."
        );

        var outputOption = new Option<DirectoryInfo>(
            aliases: ["--output", "-o"],
            description: "Output directory for the generated JAR and POM. Defaults to the current directory.",
            getDefaultValue: () => new DirectoryInfo(Directory.GetCurrentDirectory())
        );

        var command = new Command("pack", "Pack .smithy files into a Maven JAR artifact.")
        {
            projectOption,
            sourcesOption,
            groupOption,
            artifactOption,
            versionOption,
            outputOption,
        };

        command.SetHandler(
            ExecuteAsync,
            projectOption,
            sourcesOption,
            groupOption,
            artifactOption,
            versionOption,
            outputOption
        );

        return command;
    }

    private static Task ExecuteAsync(
        FileInfo? project,
        DirectoryInfo? sources,
        string? group,
        string? artifact,
        string? version,
        DirectoryInfo output
    )
    {
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        var csproj = CsprojReader.TryLoad(project, cwd);

        // Resolve each coordinate: CLI arg → csproj → error
        var resolvedSources =
            sources
            ?? (
                csproj?.Sources is string s
                    ? new DirectoryInfo(Path.Combine(cwd.FullName, s))
                    : null
            )
            ?? cwd;

        var resolvedGroup = group ?? csproj?.GroupId;
        var resolvedArtifact = artifact ?? csproj?.ArtifactId;
        var resolvedVersion = version ?? csproj?.Version;

        var missing = new List<string>();
        if (resolvedGroup is null)
            missing.Add("--group / SmithyMavenGroupId");
        if (resolvedArtifact is null)
            missing.Add("--artifact / SmithyMavenArtifactId");
        if (resolvedVersion is null)
            missing.Add("--version / VersionPrefix");

        if (missing.Count > 0)
        {
            Console.Error.WriteLine("error: the following required values could not be resolved:");
            foreach (var m in missing)
                Console.Error.WriteLine($"  {m}");
            Console.Error.WriteLine(
                "Provide them as CLI options or set the corresponding properties in your .csproj."
            );
            return Task.FromResult(1);
        }

        if (!resolvedSources.Exists)
        {
            Console.Error.WriteLine(
                $"error: sources directory '{resolvedSources.FullName}' does not exist."
            );
            return Task.FromResult(1);
        }

        var smithyFiles = resolvedSources
            .EnumerateFiles("*.smithy", SearchOption.AllDirectories)
            .OrderBy(f => f.FullName)
            .ToList();

        if (smithyFiles.Count == 0)
        {
            Console.Error.WriteLine(
                $"error: no .smithy files found in '{resolvedSources.FullName}'."
            );
            return Task.FromResult(1);
        }

        output.Create();

        var baseName = $"{resolvedArtifact}-{resolvedVersion}";
        var jarPath = Path.Combine(output.FullName, $"{baseName}.jar");
        var pomPath = Path.Combine(output.FullName, $"{baseName}.pom");

        // Build manifest: paths relative to META-INF/smithy/
        var entries = smithyFiles
            .Select(f =>
                Path.GetRelativePath(resolvedSources.FullName, f.FullName).Replace('\\', '/')
            )
            .ToList();

        var manifest = string.Join("\n", entries) + "\n";

        Console.WriteLine($"Packing {smithyFiles.Count} .smithy file(s) → {jarPath}");

        using (var jarStream = File.Create(jarPath))
        using (var zip = new ZipArchive(jarStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            WriteZipEntry(zip, "META-INF/smithy/manifest", Encoding.UTF8.GetBytes(manifest));

            foreach (var (file, entryName) in smithyFiles.Zip(entries))
            {
                var content = File.ReadAllBytes(file.FullName);
                WriteZipEntry(zip, $"META-INF/smithy/{entryName}", content);
            }
        }

        var pom = GeneratePom(resolvedGroup!, resolvedArtifact!, resolvedVersion!);
        File.WriteAllText(pomPath, pom, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        WriteChecksums(jarPath);
        WriteChecksums(pomPath);

        Console.WriteLine($"  {Path.GetFileName(jarPath)}");
        Console.WriteLine($"  {Path.GetFileName(pomPath)}");
        Console.WriteLine("Done.");

        return Task.CompletedTask;
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string GeneratePom(string group, string artifact, string version)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                XName.Get("project", "http://maven.apache.org/POM/4.0.0"),
                new XAttribute(
                    XName.Get("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance"),
                    "http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd"
                ),
                new XElement("modelVersion", "4.0.0"),
                new XElement("groupId", group),
                new XElement("artifactId", artifact),
                new XElement("version", version),
                new XElement("packaging", "jar")
            )
        );

        return doc.ToString();
    }

    private static void WriteChecksums(string filePath)
    {
        var content = File.ReadAllBytes(filePath);

        // MD5 and SHA1 are Maven's required checksum algorithms — not used for security.
#pragma warning disable CA5351, CA5350
        var md5Hex = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();
        File.WriteAllText(filePath + ".md5", md5Hex);

        var sha1Hex = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        File.WriteAllText(filePath + ".sha1", sha1Hex);
#pragma warning restore CA5351, CA5350
    }
}
