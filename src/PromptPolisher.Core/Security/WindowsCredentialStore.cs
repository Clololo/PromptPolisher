using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using PromptPolisher.Core.Abstractions;

namespace PromptPolisher.Core.Security;

/// <summary>
/// Windows Credential Manager 存储 API Key。若不可用，退化为 DPAPI（CurrentUser）加密文件。
/// 任何情况下都不明文落盘。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PromptPolisher.Secret.v1");

    private readonly IRedactingLogger _logger;
    private readonly string _fallbackDirectory;

    public WindowsCredentialStore(IRedactingLogger logger, string fallbackDirectory)
    {
        _logger = logger;
        _fallbackDirectory = fallbackDirectory;
        Directory.CreateDirectory(_fallbackDirectory);
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (CredRead(key, CredTypeGeneric, 0, out var pointer))
        {
            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
                if (credential.CredentialBlobSize == 0)
                {
                    return Task.FromResult<string?>(string.Empty);
                }

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                CredFree(pointer);
            }
        }

        var error = Marshal.GetLastWin32Error();
        if (error != 1168) // ERROR_NOT_FOUND
        {
            _logger.Warn($"Credential Manager read failed for '{key}', error={error}. Trying DPAPI fallback.");
        }

        return ReadFallbackAsync(key, cancellationToken);
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);

        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = key,
                Comment = "PromptPolisher secret",
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "PromptPolisher"
            };

            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                _logger.Warn($"Credential Manager write failed for '{key}', error={error}. Using DPAPI fallback.");
                await WriteFallbackAsync(key, bytes, cancellationToken);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }

        await Task.CompletedTask;
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!CredDelete(key, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168)
            {
                _logger.Warn($"Credential Manager delete failed for '{key}', error={error}.");
            }
        }

        var fallbackPath = GetFallbackPath(key);
        try
        {
            if (File.Exists(fallbackPath))
            {
                File.Delete(fallbackPath);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to delete DPAPI fallback file.", ex);
        }

        await Task.CompletedTask;
    }

    private async Task<string?> ReadFallbackAsync(string key, CancellationToken cancellationToken)
    {
        var path = GetFallbackPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to read DPAPI fallback secret.", ex);
            return null;
        }
    }

    private async Task WriteFallbackAsync(string key, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = GetFallbackPath(key);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, protectedBytes, cancellationToken);
    }

    private string GetFallbackPath(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_fallbackDirectory, hash + ".bin");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Comment;

        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);
}
