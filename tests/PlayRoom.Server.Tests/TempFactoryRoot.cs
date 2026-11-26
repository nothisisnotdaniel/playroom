using System.Diagnostics;

namespace PlayRoom.Server.Tests;

/// <summary>A temporary factory root with sample games and job folders, deleted on dispose. Tests never use the
/// real factory root.</summary>
internal sealed class TempFactoryRoot : IDisposable
{
    public TempFactoryRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "playroom-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>The jobs folder <c>factory/jobs</c> under the root.</summary>
    public string JobsPath => System.IO.Path.Combine(Path, "factory", "jobs");

    /// <summary>Creates a project folder with the given <c>CLAUDE.md</c> and, when given, a <c>backlog.md</c>.</summary>
    public string AddProject(string name, string claudeMd, string? backlog)
    {
        string folder = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "CLAUDE.md"), claudeMd);
        if (backlog is not null)
        {
            File.WriteAllText(System.IO.Path.Combine(folder, "backlog.md"), backlog);
        }

        return folder;
    }

    /// <summary>Writes a file of a project (a path relative to it, folders created) and returns its path.</summary>
    public static string AddFile(string projectFolder, string relativePath, string text)
    {
        string file = System.IO.Path.Combine(projectFolder, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Creates the job folder and, when given, its <c>state.json</c>; returns the folder's path.</summary>
    public string AddJob(string id, string? stateJson)
    {
        string folder = System.IO.Path.Combine(JobsPath, id);
        Directory.CreateDirectory(folder);
        if (stateJson is not null)
        {
            File.WriteAllText(System.IO.Path.Combine(folder, "state.json"), stateJson);
        }

        return folder;
    }

    /// <summary>Writes a record of a job (a path relative to the job folder) and returns its path.</summary>
    public string AddRecord(string id, string relativePath, string text)
    {
        return AddFile(System.IO.Path.Combine(JobsPath, id), relativePath, text);
    }

    /// <summary>Writes a screenshot into the job's <c>round-&lt;n&gt;/shots/</c> and returns its path.</summary>
    public string AddShot(string id, int round, string name, byte[] bytes)
    {
        string folder = System.IO.Path.Combine(JobsPath, id, "round-" + round, "shots");
        Directory.CreateDirectory(folder);
        string file = System.IO.Path.Combine(folder, name);
        File.WriteAllBytes(file, bytes);
        return file;
    }

    /// <summary>Makes the folder a git repo on <paramref name="branch"/> with a local identity and commits
    /// everything in it.</summary>
    public static void InitGit(string folder, string branch)
    {
        Git(folder, "init", "-q", "-b", branch);
        File.AppendAllText(
            System.IO.Path.Combine(folder, ".git", "config"),
            "[user]\n\tname = Test User\n\temail = test@example.invalid\n"
            + "[commit]\n\tgpgsign = false\n"
            + "[core]\n\tautocrlf = false\n\thooksPath = .git/hooks\n");
        Git(folder, "add", "-A");
        Git(folder, "commit", "-q", "-m", "init");
    }

    /// <summary>Runs git in the folder with a fixed argument list; fails the test when git fails.</summary>
    public static string Git(string folder, params string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "git " + string.Join(" ", arguments) + ": " + error.Result);
        return output.Trim();
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
