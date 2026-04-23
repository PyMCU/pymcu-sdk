# SPDX-License-Identifier: MIT
# PyMCU Backend SDK — Unit tests for ToolchainPlugin and ExternalToolchain.

from pathlib import Path
from typing import Optional
from unittest.mock import MagicMock, patch
import os

import pytest

from pymcu.toolchain.sdk.plugin import ToolchainPlugin
from pymcu.toolchain.sdk.toolchain import ExternalToolchain
from pymcu.toolchain.sdk.base_tool import CacheableTool


# ---------------------------------------------------------------------------
# Concrete stubs for testing
# ---------------------------------------------------------------------------

def _make_console() -> MagicMock:
    return MagicMock()


class _FakeToolchain(ExternalToolchain):
    """Minimal concrete ExternalToolchain for testing."""

    def get_name(self) -> str:
        return "fake-avr-gcc"

    def is_cached(self) -> bool:
        return False

    def install(self) -> Path:
        return Path("/fake/avr-gcc")

    @classmethod
    def supports(cls, chip: str) -> bool:
        return chip.lower().startswith("atmega") or chip.lower().startswith("attiny")

    def assemble(self, asm_file: Path, output_file: Optional[Path] = None) -> Path:
        return output_file or asm_file.with_suffix(".hex")


class _FakePlugin(ToolchainPlugin):
    """Concrete ToolchainPlugin that wraps _FakeToolchain."""

    family = "avr"
    description = "AVR toolchain (fake)"
    version = "1.0.0"
    default_chip = "atmega328p"

    @classmethod
    def supports(cls, chip: str) -> bool:
        return _FakeToolchain.supports(chip)

    @classmethod
    def get_toolchain(cls, console, chip: str) -> ExternalToolchain:
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": "/tmp/fake_tools"}, clear=False):
            return _FakeToolchain(console, chip)


# ---------------------------------------------------------------------------
# ToolchainPlugin.supports
# ---------------------------------------------------------------------------

class TestToolchainPluginSupports:
    def test_supported_chip_returns_true(self):
        assert _FakePlugin.supports("atmega328p") is True
        assert _FakePlugin.supports("attiny85") is True

    def test_unsupported_chip_returns_false(self):
        assert _FakePlugin.supports("pic16f84a") is False
        assert _FakePlugin.supports("stm32f4") is False


# ---------------------------------------------------------------------------
# ToolchainPlugin.get_instance (uses default_chip)
# ---------------------------------------------------------------------------

class TestToolchainPluginGetInstance:
    def test_get_instance_returns_external_toolchain(self):
        console = _make_console()
        toolchain = _FakePlugin.get_instance(console)
        assert isinstance(toolchain, ExternalToolchain)

    def test_get_instance_uses_default_chip(self):
        console = _make_console()
        toolchain = _FakePlugin.get_instance(console)
        assert toolchain.chip == _FakePlugin.default_chip


# ---------------------------------------------------------------------------
# ToolchainPlugin.get_ffi_toolchain (default returns None)
# ---------------------------------------------------------------------------

class TestToolchainPluginFfiDefault:
    def test_get_ffi_toolchain_returns_none_by_default(self):
        console = _make_console()
        result = _FakePlugin.get_ffi_toolchain(console, "atmega328p")
        assert result is None


# ---------------------------------------------------------------------------
# ExternalToolchain — link() default is None
# ---------------------------------------------------------------------------

class TestExternalToolchainLinkDefault:
    def test_link_returns_none_by_default(self, tmp_path):
        console = _make_console()
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
            tc = _FakeToolchain(console, "atmega328p")
        result = tc.link(Path("/fake/out.hex"), "atmega328p", tmp_path)
        assert result is None


# ---------------------------------------------------------------------------
# ExternalToolchain — chip attribute is stored
# ---------------------------------------------------------------------------

class TestExternalToolchainChip:
    def test_chip_stored_correctly(self, tmp_path):
        console = _make_console()
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
            tc = _FakeToolchain(console, "attiny85")
        assert tc.chip == "attiny85"

    def test_default_chip_is_empty(self, tmp_path):
        console = _make_console()
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
            tc = _FakeToolchain(console)
        assert tc.chip == ""


# ---------------------------------------------------------------------------
# ExternalToolchain — assemble() contract
# ---------------------------------------------------------------------------

class TestExternalToolchainAssemble:
    def test_assemble_returns_path(self, tmp_path):
        console = _make_console()
        asm = tmp_path / "blink.asm"
        asm.write_text("; nop")
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
            tc = _FakeToolchain(console, "atmega328p")
        result = tc.assemble(asm)
        assert isinstance(result, Path)

    def test_assemble_with_explicit_output(self, tmp_path):
        console = _make_console()
        asm = tmp_path / "blink.asm"
        out = tmp_path / "blink.hex"
        asm.write_text("; nop")
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
            tc = _FakeToolchain(console, "atmega328p")
        result = tc.assemble(asm, out)
        assert result == out
