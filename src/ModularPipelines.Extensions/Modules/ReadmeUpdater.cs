using System.Dynamic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Rocket.Surgery.ModularPipelines.Extensions.Readme;
using YamlDotNet.Serialization;

namespace Rocket.Surgery.ModularPipelines.Extensions.Modules;

public partial class ReadmeUpdater
(
ReadmeSettings readmeSettings,
    ArtifactSettings artifactSettings,
    SolutionSettings settings) : Module<ReadmeUpdater.Result>
{
    protected override async Task<Result?> ExecuteAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        if (!readmeSettings.ReadmeFilePath.Exists)
        {
            context.Logger.LogWarning("No README.md file found, skipping readme update");
            return null;
        }

        var content = await readmeSettings.ReadmeFilePath.ReadAsync(cancellationToken);
        var nukeDataRegex = MyRegex();
        var match = nukeDataRegex.Match(content);
        var yaml = string.Join(Environment.NewLine, match.Groups.Cast<Group>().Skip(1).Select(x => x.Value));
        var d = new DeserializerBuilder()
           // .WithNamingConvention(new CamelCaseNamingConvention())
           .Build();
        using var reader = new StringReader(yaml.Trim('\n', '\r'));
        var config = d.Deserialize<ExpandoObject>(reader);

        var sectionRegex = MyRegex1();

        var sections = sectionRegex.Matches(content);

        var ranges = new List<(int start, int length, string content)>();
        foreach (var sectionMatch in sections
                                    .GroupBy(x => x.Groups[1].Value)
                                    .OrderByDescending(x => x.Key != "generated references")
                )
        {
            var sectionName = sectionMatch.First().Groups[1].Value;
            if (!readmeSettings.Sections.AllSections.TryGetValue(sectionName, out var section))
                continue; // throw new NotImplementedException("Section " + sectionName + " is not supported!");

            var sectionStart = sectionMatch.First().Captures[0];
            var sectionEnd = sectionMatch.Last().Captures[0];
            var newSectionContent = await section.Process(config, readmeSettings.References, context);
            ranges.Add(
                (sectionStart.Index + sectionStart.Length,
                  sectionEnd.Index - (sectionStart.Index + sectionStart.Length), newSectionContent)
            );
        }

        foreach (var range in ranges.OrderByDescending(x => x.start))
        {
            content = string.Concat(
                content.AsSpan(0, range.start),
                Environment.NewLine,
                range.content,
                content.AsSpan(range.start + range.length)
            );
        }

        return new Result(content);
    }

    [GeneratedRegex("<!-- nuke-data(.*?)-->", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "en-US")]
    private static partial Regex MyRegex();

    [GeneratedRegex("<!-- (.*?) -->", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled, "")]
    private static partial Regex MyRegex1();

    public record Result(string Readme);
}
