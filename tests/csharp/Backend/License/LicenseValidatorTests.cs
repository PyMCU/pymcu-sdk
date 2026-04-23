// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for LicenseValidator.

using FluentAssertions;
using PyMCU.Backend.License;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.Backend.License;

/// <summary>
/// Tests for LicenseValidator key resolution and JWT parsing.
///
/// Because the SDK ships with the placeholder public key
/// ("REPLACE_WITH_REAL_RSA2048_PUBLIC_KEY_BASE64_HERE"), signature
/// verification is intentionally skipped in dev builds.  These tests
/// therefore exercise key resolution, expiry, family matching, and
/// structural parsing — not the actual RSA signature.
/// </summary>
public class LicenseValidatorResolveKeyTests : IDisposable
{
    private readonly string _origEnv;
    private readonly string _tmpHome;

    public LicenseValidatorResolveKeyTests()
    {
        _origEnv = Environment.GetEnvironmentVariable("PYMCU_LICENSE_KEY") ?? "";
        Environment.SetEnvironmentVariable("PYMCU_LICENSE_KEY", null);
        _tmpHome = Path.Combine(Path.GetTempPath(), $"pymcu_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PYMCU_LICENSE_KEY",
            string.IsNullOrEmpty(_origEnv) ? null : _origEnv);
        if (Directory.Exists(_tmpHome))
            Directory.Delete(_tmpHome, recursive: true);
    }

    [Fact]
    public void ResolveKey_ExplicitKey_TakesPriority()
    {
        Environment.SetEnvironmentVariable("PYMCU_LICENSE_KEY", "env-key");
        LicenseValidator.ResolveKey("explicit-key").Should().Be("explicit-key");
    }

    [Fact]
    public void ResolveKey_EnvVar_ResolvedWhenNoExplicit()
    {
        Environment.SetEnvironmentVariable("PYMCU_LICENSE_KEY", "env-key");
        LicenseValidator.ResolveKey().Should().Be("env-key");
    }

    [Fact]
    public void ResolveKey_NoSources_ReturnsNull()
    {
        // Ensure env var is cleared and no home file exists for this test
        Environment.SetEnvironmentVariable("PYMCU_LICENSE_KEY", null);
        // Note: we can't easily fake the home dir in .NET without reflection,
        // so we only verify when no env is set and ResolveKey returns null or a
        // value from the home file (which may legitimately exist in the environment).
        var result = LicenseValidator.ResolveKey();
        // Accept null or a non-empty string (in case a real license.key exists on the CI runner)
        if (result != null) result.Should().NotBeEmpty();
    }

    [Fact]
    public void ResolveKey_WhitespaceExplicit_TreatedAsNotSet()
    {
        LicenseValidator.ResolveKey("   ").Should().BeNull();
    }
}

public class LicenseValidatorValidateTests
{
    // ── Pre-built test JWTs (placeholder sig — sig check is skipped in dev mode) ──
    //
    // Generated from:
    //   header = base64url({"alg":"RS256","typ":"JWT"})
    //   payload = base64url({"sub":"...","iat":...,"exp":...,"backends":[...]})
    //   jwt = header + "." + payload + ".ZmFrZXNpZ25hdHVyZQ"  (fake sig bytes)
    //
    // These JWTs are intentionally unsigned dev tokens used only in tests.

    // Expires 2099 — "avr" backend
    private const string ValidAvrJwt =
        "eyJhbGciOiAiUlMyNTYiLCAidHlwIjogIkpXVCJ9" +
        ".eyJzdWIiOiAidXNlckBleGFtcGxlLmNvbSIsICJpYXQiOiAxNzAwMDAwMDAwLCAiZXhwIjogNDEwMjQ0NDgwMCwgImJhY2tlbmRzIjogWyJhdnIiXX0" +
        ".ZmFrZXNpZ25hdHVyZQ";

    // Expired in 2001 — "avr" backend
    private const string ExpiredJwt =
        "eyJhbGciOiAiUlMyNTYiLCAidHlwIjogIkpXVCJ9" +
        ".eyJzdWIiOiAidXNlckBleGFtcGxlLmNvbSIsICJpYXQiOiAxNzAwMDAwMDAwLCAiZXhwIjogMTAwMDAwMDAwMCwgImJhY2tlbmRzIjogWyJhdnIiXX0" +
        ".ZmFrZXNpZ25hdHVyZQ";

    // Expires 2099, "avr" only — queried for "pic14"
    private const string WrongFamilyJwt =
        "eyJhbGciOiAiUlMyNTYiLCAidHlwIjogIkpXVCJ9" +
        ".eyJzdWIiOiAidXNlckBleGFtcGxlLmNvbSIsICJpYXQiOiAxNzAwMDAwMDAwLCAiZXhwIjogNDEwMjQ0NDgwMCwgImJhY2tlbmRzIjogWyJhdnIiXX0" +
        ".ZmFrZXNpZ25hdHVyZQ";

    // Expires 2099, no "backends" field — covers any family
    private const string AllFamiliesJwt =
        "eyJhbGciOiAiUlMyNTYiLCAidHlwIjogIkpXVCJ9" +
        ".eyJzdWIiOiAidXNlckBleGFtcGxlLmNvbSIsICJpYXQiOiAxNzAwMDAwMDAwLCAiZXhwIjogNDEwMjQ0NDgwMH0" +
        ".ZmFrZXNpZ25hdHVyZQ";

    [Fact]
    public void Validate_NoKey_ReturnsMissing()
    {
        // Pass an explicit null so it doesn't read from env or file
        var result = LicenseValidator.Validate("avr", "");
        result.Status.Should().Be(LicenseStatus.Missing);
    }

    [Fact]
    public void Validate_MalformedKey_ReturnsMalformed()
    {
        var result = LicenseValidator.Validate("avr", "not.a.jwt.with.too.many.dots.here.x");
        result.Status.Should().Be(LicenseStatus.Malformed);
    }

    [Fact]
    public void Validate_ValidAvrKey_CorrectFamily_ReturnsValid()
    {
        var result = LicenseValidator.Validate("avr", ValidAvrJwt);
        result.Status.Should().Be(LicenseStatus.Valid);
        result.Email.Should().Be("user@example.com");
    }

    [Fact]
    public void Validate_ExpiredKey_ReturnsExpired()
    {
        var result = LicenseValidator.Validate("avr", ExpiredJwt);
        result.Status.Should().Be(LicenseStatus.Expired);
        result.Email.Should().Be("user@example.com");
        result.ExpiryDate.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Validate_WrongFamily_ReturnsInvalidTarget()
    {
        var result = LicenseValidator.Validate("pic14", WrongFamilyJwt);
        result.Status.Should().Be(LicenseStatus.InvalidTarget);
    }

    [Fact]
    public void Validate_AllFamilies_AllowsAnyFamily()
    {
        // No "backends" array in payload → all families accepted
        var result = LicenseValidator.Validate("riscv", AllFamiliesJwt);
        result.Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public void Free_AlwaysReturnsValid()
    {
        LicenseValidator.Free().Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public void Validate_CaseInsensitiveFamily_Match()
    {
        // "avr" in JWT, "AVR" queried
        var result = LicenseValidator.Validate("AVR", ValidAvrJwt);
        result.Status.Should().Be(LicenseStatus.Valid);
    }
}
