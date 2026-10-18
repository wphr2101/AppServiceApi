using CustomerOperations.Api.Security;
using FluentAssertions;

namespace CustomerOperations.Api.Tests;

public class ApiKeyValidatorTests
{
    [Fact]
    public void Validate_WhenProvidedTokenIsMissing_ReturnsMissingOrInvalidToken()
    {
        var validator = new ApiKeyValidator();

        var result = validator.Validate("secret-token", null);

        result.Status.Should().Be(ApiKeyValidationStatus.MissingOrInvalidToken);
        result.IsValid.Should().BeFalse();
        result.PublicMessage.Should().Be("Invalid or missing API token.");
    }

    [Fact]
    public void Validate_WhenProvidedTokenIsWrong_ReturnsMissingOrInvalidToken()
    {
        var validator = new ApiKeyValidator();

        var result = validator.Validate("secret-token", "wrong-token");

        result.Status.Should().Be(ApiKeyValidationStatus.MissingOrInvalidToken);
        result.IsValid.Should().BeFalse();
        result.PublicMessage.Should().Be("Invalid or missing API token.");
    }

    [Fact]
    public void Validate_WhenProvidedTokenIsCorrect_ReturnsValid()
    {
        var validator = new ApiKeyValidator();

        var result = validator.Validate("secret-token", "secret-token");

        result.Status.Should().Be(ApiKeyValidationStatus.Valid);
        result.IsValid.Should().BeTrue();
        result.PublicMessage.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenConfiguredTokenIsMissing_ReturnsTokenNotConfigured()
    {
        var validator = new ApiKeyValidator();

        var result = validator.Validate(null, "secret-token");

        result.Status.Should().Be(ApiKeyValidationStatus.TokenNotConfigured);
        result.IsValid.Should().BeFalse();
        result.PublicMessage.Should().Be("API token is not configured.");
    }
}
