# SPDX-License-Identifier: MIT
# PyMCU Backend SDK — Unit tests for BackendPlugin and resolve_license_key.

import os
import tempfile
from pathlib import Path
from unittest.mock import patch

import pytest

from pymcu.backend.sdk.plugin import (
    BackendPlugin,
    LicenseStatus,
    resolve_license_key,
)


# ---------------------------------------------------------------------------
# resolve_license_key — priority order
# ---------------------------------------------------------------------------

class TestResolveLicenseKey:
    def test_explicit_key_wins_over_env_and_file(self, tmp_path):
        env_key = "env-key-123"
        with patch.dict(os.environ, {"PYMCU_LICENSE_KEY": env_key}, clear=False):
            assert resolve_license_key("explicit-key") == "explicit-key"

    def test_env_var_used_when_no_explicit_key(self, tmp_path):
        with patch.dict(os.environ, {"PYMCU_LICENSE_KEY": "env-key-abc"}, clear=False):
            assert resolve_license_key() == "env-key-abc"

    def test_env_var_whitespace_stripped(self):
        with patch.dict(os.environ, {"PYMCU_LICENSE_KEY": "  my-key  "}, clear=False):
            assert resolve_license_key() == "my-key"

    def test_explicit_key_whitespace_stripped(self):
        assert resolve_license_key("  trimmed  ") == "trimmed"

    def test_file_used_when_no_explicit_or_env(self, tmp_path):
        key_file = tmp_path / "license.key"
        key_file.write_text("file-key-xyz\n")
        with patch.dict(os.environ, {"PYMCU_LICENSE_KEY": ""}, clear=False):
            with patch("pathlib.Path.home", return_value=tmp_path):
                # Create the expected path: ~/.pymcu/license.key
                pymcu_dir = tmp_path / ".pymcu"
                pymcu_dir.mkdir(parents=True, exist_ok=True)
                license_key_path = pymcu_dir / "license.key"
                license_key_path.write_text("file-key-xyz\n")
                result = resolve_license_key()
        # Either from env (empty after strip) or from file
        # Since env is empty, it should fall through to file
        # (result depends on whether empty string counts as set in env)
        # The implementation uses os.environ.get which returns "", truthy only if non-empty
        assert result == "file-key-xyz"

    def test_returns_none_when_nothing_set(self, tmp_path):
        with patch.dict(os.environ, {"PYMCU_LICENSE_KEY": ""}, clear=False):
            with patch("pathlib.Path.home", return_value=tmp_path):
                # No license.key file exists under tmp_path/.pymcu/
                result = resolve_license_key()
        assert result is None

    def test_explicit_none_falls_through_to_env(self):
        with patch.dict(os.environ, {"PYMCU_LICENSE_KEY": "from-env"}, clear=False):
            assert resolve_license_key(None) == "from-env"


# ---------------------------------------------------------------------------
# BackendPlugin — abstract base class contract
# ---------------------------------------------------------------------------

class _FreeBackend(BackendPlugin):
    """Minimal concrete backend for testing."""
    family = "test"
    description = "Test backend"
    version = "0.0.1"
    supported_arches = ["atmega", "attiny", "at90"]

    @classmethod
    def get_backend_binary(cls) -> Path:
        return Path("/usr/bin/fake-backend")


class _PaidBackend(BackendPlugin):
    """Backend that overrides validate_license to return MISSING."""
    family = "paid"
    description = "Paid test backend"
    version = "1.0.0"
    supported_arches = ["pic16"]

    @classmethod
    def get_backend_binary(cls) -> Path:
        return Path("/usr/bin/paid-backend")

    @classmethod
    def validate_license(cls, key=None) -> LicenseStatus:
        return LicenseStatus.MISSING


class TestBackendPluginSupports:
    def test_exact_match(self):
        assert _FreeBackend.supports("atmega") is True

    def test_prefix_match(self):
        assert _FreeBackend.supports("atmega328p") is True
        assert _FreeBackend.supports("attiny85") is True
        assert _FreeBackend.supports("at90usb") is True

    def test_case_insensitive_match(self):
        assert _FreeBackend.supports("ATmega328P") is True
        assert _FreeBackend.supports("ATTINY85") is True

    def test_unsupported_chip_returns_false(self):
        assert _FreeBackend.supports("pic16f84a") is False
        assert _FreeBackend.supports("stm32") is False
        assert _FreeBackend.supports("") is False

    def test_partial_match_does_not_false_positive(self):
        # "mega" is not a valid prefix — only "atmega" is
        assert _FreeBackend.supports("mega328") is False


class TestBackendPluginValidateLicense:
    def test_default_returns_valid(self):
        status = _FreeBackend.validate_license()
        assert status == LicenseStatus.VALID

    def test_override_can_return_missing(self):
        status = _PaidBackend.validate_license()
        assert status == LicenseStatus.MISSING

    def test_default_ignores_key_argument(self):
        # Default implementation always returns VALID regardless of key
        assert _FreeBackend.validate_license("any-key") == LicenseStatus.VALID
        assert _FreeBackend.validate_license(None) == LicenseStatus.VALID


class TestBackendPluginGetBackendBinary:
    def test_returns_path(self):
        p = _FreeBackend.get_backend_binary()
        assert isinstance(p, Path)


class TestLicenseStatusEnum:
    def test_all_expected_values_exist(self):
        assert LicenseStatus.VALID
        assert LicenseStatus.MISSING
        assert LicenseStatus.EXPIRED
        assert LicenseStatus.INVALID_TARGET
        assert LicenseStatus.MALFORMED

    def test_values_are_distinct(self):
        values = [
            LicenseStatus.VALID,
            LicenseStatus.MISSING,
            LicenseStatus.EXPIRED,
            LicenseStatus.INVALID_TARGET,
            LicenseStatus.MALFORMED,
        ]
        assert len(set(values)) == 5
