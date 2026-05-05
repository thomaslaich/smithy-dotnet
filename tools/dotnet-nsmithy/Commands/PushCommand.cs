using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;

namespace DotnetNsmithy.Commands;

internal static class PushCommand
{
    public static Command Create()
    {
        var directoryArg = new Argument<DirectoryInfo>(
            name: "directory",
            description: "Directory containing the packed artifacts (JAR, POM, and checksum files). Defaults to the current directory.",
            getDefaultValue: () => new DirectoryInfo(Directory.GetCurrentDirectory())
        );

        var projectOption = new Option<FileInfo?>(
            aliases: ["--project", "-p"],
            description: "Path to the .csproj file. Auto-discovered in the current directory when omitted."
        );

        var groupOption = new Option<string?>(
            aliases: ["--group", "-g"],
            description: "Maven groupId. Overrides SmithyMavenGroupId in the .csproj."
        );

        var artifactOption = new Option<string?>(
            aliases: ["--artifact", "-a"],
            description: "Maven artifactId. Overrides SmithyMavenArtifactId in the .csproj."
        );

        var versionOption = new Option<string?>(
            aliases: ["--version", "-v"],
            description: "Maven version. Overrides the version in the .csproj."
        );

        var registryOption = new Option<Uri>(
            aliases: ["--registry", "-r"],
            description: "Maven registry base URL (e.g. https://maven.pkg.github.com/ORG/REPO)."
        )
        {
            IsRequired = true,
        };

        var usernameOption = new Option<string?>(
            aliases: ["--username", "-u"],
            description: "Registry username. Falls back to MAVEN_USERNAME or GITHUB_ACTOR environment variable."
        );

        var tokenOption = new Option<string?>(
            aliases: ["--token", "-t"],
            description: "Registry password / token. Falls back to MAVEN_TOKEN or GITHUB_TOKEN environment variable."
        );

        var command = new Command("push", "Publish a packed Maven artifact to a Maven registry.")
        {
            directoryArg,
            projectOption,
            groupOption,
            artifactOption,
            versionOption,
            registryOption,
            usernameOption,
            tokenOption,
        };

        command.SetHandler(
            ExecuteAsync,
            directoryArg,
            projectOption,
            groupOption,
            artifactOption,
            versionOption,
            registryOption,
            usernameOption,
            tokenOption
        );

        return command;
    }

    private static async Task ExecuteAsync(
        DirectoryInfo directory,
        FileInfo? project,
        string? group,
        string? artifact,
        string? version,
        Uri registry,
        string? username,
        string? token
    )
    {
        var csproj = CsprojReader.TryLoad(project, directory);

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
            return;
        }

        username ??=
            Environment.GetEnvironmentVariable("MAVEN_USERNAME")
            ?? Environment.GetEnvironmentVariable("GITHUB_ACTOR");

        token ??=
            Environment.GetEnvironmentVariable("MAVEN_TOKEN")
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine(
                "error: credentials are required. Provide --username/--token, or set MAVEN_USERNAME/MAVEN_TOKEN (or GITHUB_ACTOR/GITHUB_TOKEN)."
            );
            return;
        }

        var baseName = $"{resolvedArtifact}-{resolvedVersion}";
        var files = new[]
        {
            $"{baseName}.pom",
            $"{baseName}.pom.md5",
            $"{baseName}.pom.sha1",
            $"{baseName}.jar",
            $"{baseName}.jar.md5",
            $"{baseName}.jar.sha1",
        };

        var groupPath = resolvedGroup!.Replace('.', '/');
        var baseUrl = registry.ToString().TrimEnd('/');
        var versionUrl = $"{baseUrl}/{groupPath}/{resolvedArtifact}/{resolvedVersion}";

        using var http = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{token}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            credentials
        );

        Console.WriteLine($"Pushing {resolvedArtifact}:{resolvedVersion} → {versionUrl}");

        foreach (var fileName in files)
        {
            var filePath = Path.Combine(directory.FullName, fileName);
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"error: expected file not found: {filePath}");
                Console.Error.WriteLine("Run 'dotnet nsmithy pack' first.");
                return;
            }

            var uploadUrl = $"{versionUrl}/{fileName}";
            Console.Write($"  PUT {fileName} ... ");

            var content = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
            content.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(fileName));

            var response = await http.PutAsync(uploadUrl, content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"FAILED ({(int)response.StatusCode} {response.ReasonPhrase})");
                Console.Error.WriteLine(body);
                return;
            }
        }

        Console.WriteLine("Done.");
    }

    private static string GetContentType(string fileName) =>
        Path.GetExtension(fileName) switch
        {
            ".pom" => "application/xml",
            ".jar" => "application/java-archive",
            _ => "text/plain",
        };
}
