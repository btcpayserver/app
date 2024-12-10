using Plugin.Fingerprint.Abstractions;

namespace BTCPayApp.Desktop;

public class StubFingerprintProvider: IFingerprint
{
    public Task<FingerprintAvailability> GetAvailabilityAsync(bool allowAlternativeAuthentication = false)
    {
        return Task.FromResult(FingerprintAvailability.NoImplementation);
    }

    public Task<bool> IsAvailableAsync(bool allowAlternativeAuthentication = false)
    {
        return Task.FromResult(false);
    }

    public Task<FingerprintAuthenticationResult> AuthenticateAsync(AuthenticationRequestConfiguration authRequestConfig,
        CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public Task<AuthenticationType> GetAuthenticationTypeAsync()
    {
        throw new NotImplementedException();
    }
}