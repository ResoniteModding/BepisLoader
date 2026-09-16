using Cake.Common;
using Cake.Core;
using Cake.Core.IO;

/// <summary>Represents a Git commit.</summary>
/// <param name="Sha">The full commit hash, represented as a hexadecimal string.</param>
public sealed record GitCommit(string Sha);
/// <summary>Represents a Git branch.</summary>
/// <param name="FriendlyName">The branch friendly name.</param>
public sealed record GitBranch(string FriendlyName);

static class GitTasks
{
    public static string Git(this ICakeContext ctx, string args, string separator = "")
    {
        using var process = ctx.StartAndReturnProcess("git", new() { Arguments = args, RedirectStandardOutput = true });
        process.WaitForExit();
        return string.Join(separator, process.GetStandardOutput());
    }

    public static GitCommit GitLogTip(this ICakeContext ctx, DirectoryPath repository) =>
        new(ctx.Git($"-C \"{repository.FullPath}\" rev-parse HEAD").Trim());

    public static GitBranch GitBranchCurrent(this ICakeContext ctx, DirectoryPath repository) =>
        new(ctx.Git($"-C \"{repository.FullPath}\" rev-parse --abbrev-ref HEAD").Trim());

    public static string GitShortenSha(this ICakeContext ctx, DirectoryPath repository, GitCommit commit) =>
        ctx.Git($"-C \"{repository.FullPath}\" rev-parse --short=7 {commit.Sha}").Trim();
}
