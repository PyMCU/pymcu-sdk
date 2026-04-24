# SPDX-License-Identifier: MIT
# PyMCU Plugin SDK — Unit tests for PyPIToolchain and ToolchainNotInstalledError.

import importlib
import os
import sys
from pathlib import Path
from typing import Optional
from unittest.mock import MagicMock, patch, call

import pytest

from pymcu.toolchain.sdk.pypi_tool import PyPIToolchain, ToolchainNotInstalledError


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _make_console() -> MagicMock:
    """Return a Mock that satisfies the rich.console.Console interface."""
    return MagicMock()


def _make_tool(tmp_path: Path, console=None, *, chip: str = "atmega328p") -> "ConcreteToolchain":
    """Instantiate a concrete PyPIToolchain subclass for testing."""
    if console is None:
        console = _make_console()
    with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
        return ConcreteToolchain(console, chip)


class ConcreteToolchain(PyPIToolchain):
    """Minimal concrete subclass used by all tests."""

    pypi_package = "pymcu-fake-toolchain"
    import_name = "pymcu_fake_toolchain"
    min_version = ""

    @classmethod
    def supports(cls, chip: str) -> bool:
        return chip.lower().startswith("atmega")

    def assemble(self, asm_file: Path, output_file: Optional[Path] = None) -> Path:
        return output_file or asm_file.with_suffix(".hex")


# ---------------------------------------------------------------------------
# ToolchainNotInstalledError
# ---------------------------------------------------------------------------

class TestToolchainNotInstalledError:
    def test_stores_pypi_package(self):
        err = ToolchainNotInstalledError("my-pkg")
        assert err.pypi_package == "my-pkg"

    def test_message_contains_package_name(self):
        err = ToolchainNotInstalledError("my-pkg")
        assert "my-pkg" in str(err)

    def test_message_contains_pip_command(self):
        err = ToolchainNotInstalledError("my-pkg")
        assert "pip install" in str(err)

    def test_is_runtime_error(self):
        assert isinstance(ToolchainNotInstalledError("x"), RuntimeError)


# ---------------------------------------------------------------------------
# get_name
# ---------------------------------------------------------------------------

class TestGetName:
    def test_returns_pypi_package(self, tmp_path):
        tool = _make_tool(tmp_path)
        assert tool.get_name() == "pymcu-fake-toolchain"


# ---------------------------------------------------------------------------
# is_cached
# ---------------------------------------------------------------------------

class TestIsCached:
    def test_returns_false_when_package_not_found(self, tmp_path):
        tool = _make_tool(tmp_path)
        with patch(
            "importlib.metadata.version",
            side_effect=importlib.metadata.PackageNotFoundError("pymcu-fake-toolchain"),
        ):
            assert tool.is_cached() is False

    def test_returns_false_when_module_has_no_get_bin_dir(self, tmp_path):
        tool = _make_tool(tmp_path)
        fake_mod = MagicMock(spec=[])  # no get_bin_dir attribute
        with patch("importlib.metadata.version", return_value="1.0.0"), \
             patch("importlib.import_module", return_value=fake_mod):
            assert tool.is_cached() is False

    def test_returns_false_when_bin_dir_does_not_exist(self, tmp_path):
        tool = _make_tool(tmp_path)
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = tmp_path / "nonexistent_bin"
        with patch("importlib.metadata.version", return_value="1.0.0"), \
             patch("importlib.import_module", return_value=fake_mod):
            assert tool.is_cached() is False

    def test_returns_true_when_package_installed_and_bin_dir_exists(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = bin_dir
        with patch("importlib.metadata.version", return_value="1.0.0"), \
             patch("importlib.import_module", return_value=fake_mod):
            assert tool.is_cached() is True

    def test_returns_false_when_import_raises(self, tmp_path):
        tool = _make_tool(tmp_path)
        with patch("importlib.metadata.version", return_value="1.0.0"), \
             patch("importlib.import_module", side_effect=ImportError("no module")):
            assert tool.is_cached() is False

    def test_min_version_check_too_low(self, tmp_path):
        """Package installed but version is below min_version."""
        tool = _make_tool(tmp_path)
        tool.min_version = "2.0.0"
        with patch("importlib.metadata.version", return_value="1.0.0"):
            try:
                from packaging.version import Version  # noqa: F401
                assert tool.is_cached() is False
            except ImportError:
                pytest.skip("packaging not installed")

    def test_min_version_check_meets_requirement(self, tmp_path):
        """Package installed and version meets or exceeds min_version."""
        tool = _make_tool(tmp_path)
        tool.min_version = "1.0.0"
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = bin_dir
        with patch("importlib.metadata.version", return_value="2.0.0"), \
             patch("importlib.import_module", return_value=fake_mod):
            try:
                from packaging.version import Version  # noqa: F401
                assert tool.is_cached() is True
            except ImportError:
                pytest.skip("packaging not installed")


# ---------------------------------------------------------------------------
# get_bin_dir
# ---------------------------------------------------------------------------

class TestGetBinDir:
    def test_delegates_to_installed_package(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = bin_dir

        with patch.object(tool, "is_cached", return_value=True), \
             patch("importlib.import_module", return_value=fake_mod):
            result = tool.get_bin_dir()

        assert result == bin_dir

    def test_calls_prompt_when_not_cached(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = bin_dir

        call_log = []

        def fake_prompt():
            call_log.append("prompted")

        with patch.object(tool, "is_cached", return_value=False), \
             patch.object(tool, "_prompt_and_maybe_install", side_effect=fake_prompt), \
             patch("importlib.import_module", return_value=fake_mod):
            result = tool.get_bin_dir()

        assert "prompted" in call_log
        assert result == bin_dir


# ---------------------------------------------------------------------------
# get_tool
# ---------------------------------------------------------------------------

class TestGetTool:
    def test_returns_path_inside_bin_dir(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()

        with patch.object(tool, "get_bin_dir", return_value=bin_dir):
            result = tool.get_tool("avr-gcc")

        assert result == bin_dir / "avr-gcc"

    def test_appends_exe_on_windows(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()

        with patch.object(tool, "get_bin_dir", return_value=bin_dir), \
             patch("sys.platform", "win32"):
            result = tool.get_tool("avr-gcc")

        assert result.name == "avr-gcc.exe"

    def test_does_not_double_append_exe(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()

        with patch.object(tool, "get_bin_dir", return_value=bin_dir), \
             patch("sys.platform", "win32"):
            result = tool.get_tool("avr-gcc.exe")

        assert result.name == "avr-gcc.exe"

    def test_no_exe_on_linux(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()

        with patch.object(tool, "get_bin_dir", return_value=bin_dir), \
             patch("sys.platform", "linux"):
            result = tool.get_tool("avr-gcc")

        assert result.name == "avr-gcc"


# ---------------------------------------------------------------------------
# install
# ---------------------------------------------------------------------------

class TestInstall:
    def test_calls_pip_subprocess(self, tmp_path):
        tool = _make_tool(tmp_path)
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = bin_dir

        mock_result = MagicMock()
        mock_result.returncode = 0

        with patch("subprocess.run", return_value=mock_result) as mock_run, \
             patch("importlib.import_module", return_value=fake_mod), \
             patch("importlib.invalidate_caches"):
            result = tool.install()

        assert result == bin_dir
        mock_run.assert_called_once()
        cmd_args = mock_run.call_args[0][0]
        assert sys.executable in cmd_args
        assert "pip" in cmd_args
        assert "install" in cmd_args
        assert "pymcu-fake-toolchain" in cmd_args

    def test_raises_on_pip_failure(self, tmp_path):
        tool = _make_tool(tmp_path)

        mock_result = MagicMock()
        mock_result.returncode = 1
        mock_result.stderr = b"ERROR: Could not find a version"

        with patch("subprocess.run", return_value=mock_result):
            with pytest.raises(RuntimeError, match="pip install"):
                tool.install()

    def test_includes_min_version_in_spec(self, tmp_path):
        tool = _make_tool(tmp_path)
        tool.min_version = "2.0.0"
        bin_dir = tmp_path / "bin"
        bin_dir.mkdir()
        fake_mod = MagicMock()
        fake_mod.get_bin_dir.return_value = bin_dir

        mock_result = MagicMock()
        mock_result.returncode = 0

        with patch("subprocess.run", return_value=mock_result) as mock_run, \
             patch("importlib.import_module", return_value=fake_mod), \
             patch("importlib.invalidate_caches"):
            tool.install()

        cmd_args = mock_run.call_args[0][0]
        assert any(">=2.0.0" in arg for arg in cmd_args)


# ---------------------------------------------------------------------------
# _prompt_and_maybe_install
# ---------------------------------------------------------------------------

class TestPromptAndMaybeInstall:
    def test_auto_accepts_in_ci_mode(self, tmp_path):
        """In CI (non-interactive) mode, install proceeds without prompting."""
        tool = _make_tool(tmp_path)
        installed = []

        def fake_install():
            installed.append(True)
            return tmp_path / "bin"

        with patch(
            "pymcu.toolchain.sdk.pypi_tool._is_non_interactive", return_value=True
        ), patch.object(tool, "install", side_effect=fake_install):
            tool._prompt_and_maybe_install()

        assert installed, "install() should have been called automatically in CI"

    def test_auto_accepts_when_pymcu_no_interactive_set(self, tmp_path):
        tool = _make_tool(tmp_path)
        installed = []

        def fake_install():
            installed.append(True)
            return tmp_path / "bin"

        with patch.dict(os.environ, {"PYMCU_NO_INTERACTIVE": "1", "CI": ""}, clear=False), \
             patch.object(tool, "install", side_effect=fake_install):
            tool._prompt_and_maybe_install()

        assert installed

    def test_raises_when_user_declines(self, tmp_path):
        """User enters 'n' → ToolchainNotInstalledError is raised."""
        tool = _make_tool(tmp_path)

        with patch(
            "pymcu.toolchain.sdk.pypi_tool._is_non_interactive", return_value=False
        ), patch("builtins.input", return_value="n"):
            with pytest.raises(ToolchainNotInstalledError) as exc_info:
                tool._prompt_and_maybe_install()

        assert exc_info.value.pypi_package == "pymcu-fake-toolchain"

    def test_raises_when_user_enters_empty_string(self, tmp_path):
        """Empty input defaults to 'no'."""
        tool = _make_tool(tmp_path)

        with patch(
            "pymcu.toolchain.sdk.pypi_tool._is_non_interactive", return_value=False
        ), patch("builtins.input", return_value=""):
            with pytest.raises(ToolchainNotInstalledError):
                tool._prompt_and_maybe_install()

    def test_calls_install_when_user_accepts(self, tmp_path):
        """User enters 'y' → install() is called."""
        tool = _make_tool(tmp_path)
        installed = []

        def fake_install():
            installed.append(True)
            return tmp_path / "bin"

        with patch(
            "pymcu.toolchain.sdk.pypi_tool._is_non_interactive", return_value=False
        ), patch("builtins.input", return_value="y"), \
             patch.object(tool, "install", side_effect=fake_install):
            tool._prompt_and_maybe_install()

        assert installed

    def test_handles_eof_error_as_decline(self, tmp_path):
        """EOFError during input() is treated as 'no'."""
        tool = _make_tool(tmp_path)

        with patch(
            "pymcu.toolchain.sdk.pypi_tool._is_non_interactive", return_value=False
        ), patch("builtins.input", side_effect=EOFError):
            with pytest.raises(ToolchainNotInstalledError):
                tool._prompt_and_maybe_install()

    def test_handles_keyboard_interrupt_as_decline(self, tmp_path):
        """KeyboardInterrupt during input() is treated as 'no'."""
        tool = _make_tool(tmp_path)

        with patch(
            "pymcu.toolchain.sdk.pypi_tool._is_non_interactive", return_value=False
        ), patch("builtins.input", side_effect=KeyboardInterrupt):
            with pytest.raises(ToolchainNotInstalledError):
                tool._prompt_and_maybe_install()


# ---------------------------------------------------------------------------
# ToolchainPlugin — pip_package and pip_extras attributes
# ---------------------------------------------------------------------------

class TestToolchainPluginPipAttributes:
    def test_pip_package_defaults_to_none(self):
        from pymcu.toolchain.sdk.plugin import ToolchainPlugin

        class _Minimal(ToolchainPlugin):
            family = "test"
            description = "test"
            version = "0.0.0"

            @classmethod
            def supports(cls, chip): return False

            @classmethod
            def get_toolchain(cls, console, chip): return MagicMock()

        assert _Minimal.pip_package is None

    def test_pip_extras_defaults_to_none(self):
        from pymcu.toolchain.sdk.plugin import ToolchainPlugin

        class _Minimal(ToolchainPlugin):
            family = "test"
            description = "test"
            version = "0.0.0"

            @classmethod
            def supports(cls, chip): return False

            @classmethod
            def get_toolchain(cls, console, chip): return MagicMock()

        assert _Minimal.pip_extras is None

    def test_subclass_can_set_pip_package(self):
        from pymcu.toolchain.sdk.plugin import ToolchainPlugin

        class _WithPip(ToolchainPlugin):
            family = "avr"
            description = "AVR"
            version = "15.2.0"
            pip_package = "pymcu-avr-toolchain"
            pip_extras = ["gdb"]

            @classmethod
            def supports(cls, chip): return chip.startswith("atmega")

            @classmethod
            def get_toolchain(cls, console, chip): return MagicMock()

        assert _WithPip.pip_package == "pymcu-avr-toolchain"
        assert _WithPip.pip_extras == ["gdb"]


# ---------------------------------------------------------------------------
# Public __init__ exports
# ---------------------------------------------------------------------------

class TestPublicExports:
    def test_pypi_toolchain_exported(self):
        from pymcu.toolchain import sdk
        assert hasattr(sdk, "PyPIToolchain")
        assert sdk.PyPIToolchain is PyPIToolchain

    def test_toolchain_not_installed_error_exported(self):
        from pymcu.toolchain import sdk
        assert hasattr(sdk, "ToolchainNotInstalledError")
        assert sdk.ToolchainNotInstalledError is ToolchainNotInstalledError
