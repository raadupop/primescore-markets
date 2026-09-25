using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using PrimeScore.Engine.Host.Security;

namespace PrimeScore.Engine.Host.Cli;

internal static class OperatorSetup
{
    public static int Run(string[] args)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("Run set-operator-password in an interactive terminal. The password is never read from command-line arguments.");
        }

        var name = args is ["--name", var supplied] ? supplied : "operator";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) { throw new ArgumentException("Operator name must contain 1 to 100 characters."); }
        Console.Write("New password (12 to 1024 characters): ");
        var password = ReadPassword();
        Console.Write("Confirm password: ");
        var confirmation = ReadPassword();
        if (password.Length is < 12 or > 1024 || password != confirmation) { throw new ArgumentException("Passwords must match and contain 12 to 1024 characters."); }
        var account = new OperatorOptions { Name = name.Trim() };
        account.PasswordHash = new PasswordHasher<OperatorOptions>().HashPassword(account, password);
        var directory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "primescore-engine-host")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets", "primescore-engine-host");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "secrets.json");
        var secrets = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject();
        secrets["Auth:Operator:Name"] = account.Name;
        secrets["Auth:Operator:PasswordHash"] = account.PasswordHash;
        var temporary = path + ".new";
        File.WriteAllText(temporary, secrets.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows()) { File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        File.Move(temporary, path, overwrite: true);
        Console.WriteLine("Operator credentials saved in user-secrets. Restart with the Development launch profile, then open /login.");
        return 0;
    }

    private static string ReadPassword()
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace && value.Length > 0) { value.Length--; }
            else if (!char.IsControl(key.KeyChar)) { value.Append(key.KeyChar); }
        }
    }
}
