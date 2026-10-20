using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;
using static SystemResourceMonitor.Desktop.Adapters.Codex.CodexNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Codex;

/// <summary>Decides whether an executable carries the publisher signature of the official client.</summary>
internal interface ICodexSignatureVerifier
{
    /// <summary>Verifies the open file at <paramref name="path"/>; the handle keeps it unchanged during the check.</summary>
    bool IsTrusted(SafeFileHandle file, string path);
}

/// <summary>
/// Authenticode policy through <c>WinVerifyTrust</c>, then an exact match of the verified signer's subject with the
/// signer recorded in the B010 report. Revocation is not checked and no URL is retrieved, so verification never
/// reaches the network.
/// </summary>
internal sealed class AuthenticodeSignatureVerifier : ICodexSignatureVerifier
{
    /// <summary>The signer of the qualified standalone client.</summary>
    public const string OpenAiSigner = "CN=\"OpenAI OpCo, LLC\", O=\"OpenAI OpCo, LLC\", L=San Francisco, S=California, C=US";

    public unsafe bool IsTrusted(SafeFileHandle file, string path)
    {
        var added = false;
        file.DangerousAddRef(ref added);
        try
        {
            fixed (char* pathPointer = path)
            {
                var fileInfo = new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                    pcwszFilePath = (nint)pathPointer,
                    hFile = file.DangerousGetHandle(),
                };
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)sizeof(WINTRUST_DATA),
                    dwUIChoice = WTD_UI_NONE,
                    fdwRevocationChecks = WTD_REVOKE_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = (nint)(&fileInfo),
                    dwStateAction = WTD_STATEACTION_VERIFY,
                    dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL,
                };
                var action = GenericVerifyV2;
                try
                {
                    return WinVerifyTrust(0, &action, &data) == 0 && SignerSubject(data.hWVTStateData) == OpenAiSigner;
                }
                finally
                {
                    // Releases the provider state that VERIFY kept for the signer lookup.
                    data.dwStateAction = WTD_STATEACTION_CLOSE;
                    _ = WinVerifyTrust(0, &action, &data);
                }
            }
        }
        finally
        {
            if (added)
            {
                file.DangerousRelease();
            }
        }
    }

    /// <summary>The subject of the leaf certificate of the first signer, from the state of a successful verification.</summary>
    private static unsafe string? SignerSubject(nint state)
    {
        var provider = WTHelperProvDataFromStateData(state);
        var signer = provider == 0 ? null : (CRYPT_PROVIDER_SGNR*)WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        if (signer is null || signer->csCertChain == 0 || signer->pasCertChain == 0)
        {
            return null;
        }

        var leaf = (CRYPT_PROVIDER_CERT*)signer->pasCertChain;
        if (leaf->pCert == 0)
        {
            return null;
        }

        // The constructor duplicates the context, so the certificate outlives the provider state it came from.
        using var certificate = new X509Certificate2(leaf->pCert);
        return certificate.Subject;
    }
}
