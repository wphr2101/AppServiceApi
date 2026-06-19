namespace AppServiceApi.Security;

public enum ApiKeyValidationStatus
{
    Valid,
    TokenNotConfigured,
    MissingOrInvalidToken
}

public sealed record ApiKeyValidationResult(ApiKeyValidationStatus Status)
{
    public bool IsValid => Status == ApiKeyValidationStatus.Valid;

    public string PublicMessage => Status switch
    {
        ApiKeyValidationStatus.TokenNotConfigured => "API token is not configured.",
        ApiKeyValidationStatus.MissingOrInvalidToken => "Invalid or missing API token.",
        _ => string.Empty
    };
}

public sealed class ApiKeyValidator
{
    public ApiKeyValidationResult Validate(string? configuredToken, string? providedToken)
    {
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return new ApiKeyValidationResult(ApiKeyValidationStatus.TokenNotConfigured);
        }

        if (string.IsNullOrWhiteSpace(providedToken))
        {
            return new ApiKeyValidationResult(ApiKeyValidationStatus.MissingOrInvalidToken);
        }

        return string.Equals(providedToken, configuredToken, StringComparison.Ordinal)
            ? new ApiKeyValidationResult(ApiKeyValidationStatus.Valid)
            : new ApiKeyValidationResult(ApiKeyValidationStatus.MissingOrInvalidToken);
    }
}
