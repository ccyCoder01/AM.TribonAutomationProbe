using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AM.TribonAutomationProbe.Desktop.Services;

public sealed record AssistantModelConfigurationSnapshot(
    string BaseUrl,
    string Model,
    bool HasCredential);

public sealed class AssistantModelConfigurationStore
{
    private const int SchemaVersion = 1;
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes(
            "AM.TribonAutomationProbe.Desktop.AssistantModelConfiguration.v1");

    private readonly string _settingsPath;

    public AssistantModelConfigurationStore(
        string? settingsPath = null)
    {
        _settingsPath =
            string.IsNullOrWhiteSpace(settingsPath)
                ? GetDefaultSettingsPath()
                : Path.GetFullPath(settingsPath);
    }

    public string SettingsPath => _settingsPath;

    public AssistantModelConfigurationSnapshot LoadSnapshot()
    {
        var document = LoadDocument();

        return new AssistantModelConfigurationSnapshot(
            document.BaseUrl ?? string.Empty,
            document.Model ?? string.Empty,
            !string.IsNullOrWhiteSpace(
                document.ProtectedCredential));
    }

    public bool HasStoredCredential() =>
        !string.IsNullOrWhiteSpace(
            LoadDocument().ProtectedCredential);

    public SecureString? LoadCredential()
    {
        var document = LoadDocument();

        if (string.IsNullOrWhiteSpace(
                document.ProtectedCredential))
        {
            return null;
        }

        byte[] protectedBytes;
        try
        {
            protectedBytes =
                Convert.FromBase64String(
                    document.ProtectedCredential);
        }
        catch (FormatException)
        {
            return null;
        }

        byte[] clearBytes;
        try
        {
            clearBytes = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }

        try
        {
            if (clearBytes.Length == 0 ||
                clearBytes.Length % 2 != 0)
            {
                return null;
            }

            var credential = new SecureString();

            for (var index = 0;
                 index < clearBytes.Length;
                 index += 2)
            {
                var character =
                    (char)(
                        clearBytes[index] |
                        (clearBytes[index + 1] << 8));

                if (character == '\0')
                {
                    break;
                }

                credential.AppendChar(character);
            }

            if (credential.Length == 0)
            {
                credential.Dispose();
                return null;
            }

            credential.MakeReadOnly();
            return credential;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public void Save(
        string baseUrl,
        string model,
        SecureString? credential = null)
    {
        var current = LoadDocument();
        var protectedCredential =
            current.ProtectedCredential;

        if (credential is { Length: > 0 })
        {
            protectedCredential =
                ProtectCredential(credential);
        }

        SaveDocument(
            new ConfigurationDocument
            {
                SchemaVersion = SchemaVersion,
                BaseUrl = baseUrl ?? string.Empty,
                Model = model ?? string.Empty,
                ProtectedCredential =
                    protectedCredential ?? string.Empty
            });
    }

    public void ClearCredential(
        string baseUrl,
        string model)
    {
        SaveDocument(
            new ConfigurationDocument
            {
                SchemaVersion = SchemaVersion,
                BaseUrl = baseUrl ?? string.Empty,
                Model = model ?? string.Empty,
                ProtectedCredential = string.Empty
            });
    }

    private static string GetDefaultSettingsPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            root,
            "AM.TribonAutomationProbe",
            "assistant-model-settings.json");
    }

    private ConfigurationDocument LoadDocument()
    {
        if (!File.Exists(_settingsPath))
        {
            return new ConfigurationDocument
            {
                SchemaVersion = SchemaVersion
            };
        }

        try
        {
            var json = File.ReadAllText(
                _settingsPath,
                Encoding.UTF8);

            return JsonSerializer.Deserialize<ConfigurationDocument>(
                       json) ??
                   new ConfigurationDocument
                   {
                       SchemaVersion = SchemaVersion
                   };
        }
        catch (
            Exception exception)
            when (
                exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return new ConfigurationDocument
            {
                SchemaVersion = SchemaVersion
            };
        }
    }

    private void SaveDocument(
        ConfigurationDocument document)
    {
        var directory = Path.GetDirectoryName(
            _settingsPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(
            document,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var temporaryPath =
            _settingsPath + ".tmp";

        File.WriteAllText(
            temporaryPath,
            json,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        File.Move(
            temporaryPath,
            _settingsPath,
            overwrite: true);
    }

    private static string ProtectCredential(
        SecureString credential)
    {
        IntPtr pointer = IntPtr.Zero;
        byte[]? clearBytes = null;
        byte[]? protectedBytes = null;

        try
        {
            pointer =
                Marshal.SecureStringToCoTaskMemUnicode(
                    credential);

            clearBytes =
                new byte[credential.Length * 2];

            Marshal.Copy(
                pointer,
                clearBytes,
                0,
                clearBytes.Length);

            protectedBytes =
                ProtectedData.Protect(
                    clearBytes,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(
                protectedBytes);
        }
        finally
        {
            if (clearBytes is not null)
            {
                CryptographicOperations.ZeroMemory(
                    clearBytes);
            }

            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(
                    protectedBytes);
            }

            if (pointer != IntPtr.Zero)
            {
                Marshal.ZeroFreeCoTaskMemUnicode(
                    pointer);
            }
        }
    }

    private sealed class ConfigurationDocument
    {
        public int SchemaVersion { get; set; }

        public string? BaseUrl { get; set; }

        public string? Model { get; set; }

        public string? ProtectedCredential { get; set; }
    }
}
