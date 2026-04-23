// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for LicenseResult factory methods.

using FluentAssertions;
using PyMCU.Backend.License;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.Backend.License;

public class LicenseResultTests
{
    [Fact]
    public void Free_ReturnsValidStatus_WithFreeMessage()
    {
        var result = LicenseResult.Free();

        result.Status.Should().Be(LicenseStatus.Valid);
        result.Message.Should().Be("free");
        result.Email.Should().BeNull();
        result.ExpiryDate.Should().BeNull();
    }

    [Fact]
    public void Ok_ReturnsValidStatus_WithEmailAndExpiry()
    {
        var expiry = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = LicenseResult.Ok("dev@example.com", expiry);

        result.Status.Should().Be(LicenseStatus.Valid);
        result.Email.Should().Be("dev@example.com");
        result.ExpiryDate.Should().Be(expiry);
    }

    [Fact]
    public void NotFound_ReturnsMissingStatus()
    {
        var result = LicenseResult.NotFound("avr");

        result.Status.Should().Be(LicenseStatus.Missing);
        result.Message.Should().Contain("avr");
        result.Message.Should().Contain("PYMCU_LICENSE_KEY");
    }

    [Fact]
    public void Expired_ReturnsExpiredStatus_WithEmailAndDate()
    {
        var expiry = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = LicenseResult.Expired("user@example.com", expiry);

        result.Status.Should().Be(LicenseStatus.Expired);
        result.Email.Should().Be("user@example.com");
        result.ExpiryDate.Should().Be(expiry);
        result.Message.Should().Contain("2024-06-01");
    }

    [Fact]
    public void WrongFamily_ReturnsInvalidTargetStatus()
    {
        var result = LicenseResult.WrongFamily("pic14");

        result.Status.Should().Be(LicenseStatus.InvalidTarget);
        result.Message.Should().Contain("pic14");
    }
}
