# SPDX-License-Identifier: MIT
# PyMCU Backend SDK — Unit tests for CacheableTool (base_tool.py).

import hashlib
import io
import os
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _make_console() -> MagicMock:
    """Return a Mock that satisfies the rich.console.Console interface."""
    return MagicMock()


def _make_tool(tmp_path: Path, console=None):
    """Create a concrete CacheableTool subclass for testing."""
    from pymcu.toolchain.sdk.base_tool import CacheableTool

    class _DummyTool(CacheableTool):
        def get_name(self) -> str:
            return "dummy"

        def is_cached(self) -> bool:
            return (self._get_tool_dir() / "dummy_binary").exists()

        def install(self) -> Path:
            binary = self._get_tool_dir() / "dummy_binary"
            binary.parent.mkdir(parents=True, exist_ok=True)
            binary.write_text("dummy")
            return binary

    if console is None:
        console = _make_console()

    with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": str(tmp_path)}, clear=False):
        return _DummyTool(console)


# ---------------------------------------------------------------------------
# _default_platform_key
# ---------------------------------------------------------------------------

class TestDefaultPlatformKey:
    def test_returns_os_arch_format(self):
        from pymcu.toolchain.sdk.base_tool import _default_platform_key
        key = _default_platform_key()
        assert "-" in key, "Platform key should be '<os>-<arch>'"

    @pytest.mark.parametrize("machine,expected_arch", [
        ("x86_64", "x86_64"),
        ("AMD64",  "x86_64"),
        ("aarch64","arm64"),
        ("arm64",  "arm64"),
        ("riscv64","riscv64"),  # unknown → passed through
    ])
    def test_architecture_normalisation(self, machine, expected_arch):
        from pymcu.toolchain.sdk.base_tool import _default_platform_key
        with patch("platform.machine", return_value=machine):
            key = _default_platform_key()
        assert key.endswith(f"-{expected_arch}"), f"Got {key!r}"

    def test_linux_os_normalised_to_linux(self):
        from pymcu.toolchain.sdk.base_tool import _default_platform_key
        with patch("sys.platform", "linux"), patch("platform.machine", return_value="x86_64"):
            key = _default_platform_key()
        assert key.startswith("linux-")

    def test_darwin_os_kept_as_darwin(self):
        from pymcu.toolchain.sdk.base_tool import _default_platform_key
        with patch("sys.platform", "darwin"), patch("platform.machine", return_value="arm64"):
            key = _default_platform_key()
        assert key.startswith("darwin-")


# ---------------------------------------------------------------------------
# _is_non_interactive
# ---------------------------------------------------------------------------

class TestIsNonInteractive:
    def test_ci_env_var_true_returns_non_interactive(self):
        from pymcu.toolchain.sdk.base_tool import _is_non_interactive
        with patch.dict(os.environ, {"CI": "true"}, clear=False):
            assert _is_non_interactive() is True

    def test_pymcu_no_interactive_env_returns_non_interactive(self):
        from pymcu.toolchain.sdk.base_tool import _is_non_interactive
        with patch.dict(os.environ, {"PYMCU_NO_INTERACTIVE": "1", "CI": ""}, clear=False):
            assert _is_non_interactive() is True

    def test_non_tty_stdin_returns_non_interactive(self):
        from pymcu.toolchain.sdk.base_tool import _is_non_interactive
        mock_stdin = MagicMock()
        mock_stdin.isatty.return_value = False
        with patch.dict(os.environ, {"CI": "", "PYMCU_NO_INTERACTIVE": ""}, clear=False):
            with patch("sys.stdin", mock_stdin):
                assert _is_non_interactive() is True

    def test_tty_stdin_returns_interactive(self):
        from pymcu.toolchain.sdk.base_tool import _is_non_interactive
        mock_stdin = MagicMock()
        mock_stdin.isatty.return_value = True
        with patch.dict(os.environ, {"CI": "", "PYMCU_NO_INTERACTIVE": ""}, clear=False):
            with patch("sys.stdin", mock_stdin):
                assert _is_non_interactive() is False


# ---------------------------------------------------------------------------
# verify_sha256
# ---------------------------------------------------------------------------

class TestVerifySha256:
    def test_correct_hash_returns_true(self, tmp_path):
        tool = _make_tool(tmp_path)
        content = b"hello pymcu"
        f = tmp_path / "test.bin"
        f.write_bytes(content)
        expected = hashlib.sha256(content).hexdigest()
        assert tool.verify_sha256(f, expected) is True

    def test_uppercase_hash_also_matches(self, tmp_path):
        tool = _make_tool(tmp_path)
        content = b"hello pymcu"
        f = tmp_path / "test.bin"
        f.write_bytes(content)
        expected = hashlib.sha256(content).hexdigest().upper()
        assert tool.verify_sha256(f, expected) is True

    def test_wrong_hash_returns_false(self, tmp_path):
        tool = _make_tool(tmp_path)
        f = tmp_path / "test.bin"
        f.write_bytes(b"hello")
        assert tool.verify_sha256(f, "deadbeef" * 8) is False

    def test_missing_file_returns_false(self, tmp_path):
        tool = _make_tool(tmp_path)
        assert tool.verify_sha256(tmp_path / "nonexistent.bin", "aabbcc") is False

    def test_empty_file_has_known_sha256(self, tmp_path):
        tool = _make_tool(tmp_path)
        f = tmp_path / "empty.bin"
        f.write_bytes(b"")
        # SHA-256 of empty string is well-known
        expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        assert tool.verify_sha256(f, expected) is True


# ---------------------------------------------------------------------------
# _extract_archive — tar.gz, tar.bz2, zip (including zip-slip protection)
# ---------------------------------------------------------------------------

def _create_tar_gz(archive_path: Path, members: dict[str, bytes]):
    """Create a tar.gz archive with the given {filename: content} members."""
    with tarfile.open(archive_path, "w:gz") as tar:
        for name, data in members.items():
            info = tarfile.TarInfo(name=name)
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))


def _create_tar_bz2(archive_path: Path, members: dict[str, bytes]):
    with tarfile.open(archive_path, "w:bz2") as tar:
        for name, data in members.items():
            info = tarfile.TarInfo(name=name)
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))


def _create_zip(archive_path: Path, members: dict[str, bytes]):
    with zipfile.ZipFile(archive_path, "w") as zf:
        for name, data in members.items():
            zf.writestr(name, data)


class TestExtractArchiveTarGz:
    def test_extracts_files(self, tmp_path):
        tool = _make_tool(tmp_path)
        archive = tmp_path / "test.tar.gz"
        dest = tmp_path / "out"
        dest.mkdir()
        _create_tar_gz(archive, {"hello.txt": b"world"})
        tool._extract_archive(archive, dest, "tar.gz")
        assert (dest / "hello.txt").read_bytes() == b"world"

    def test_zipslip_member_skipped(self, tmp_path):
        """A path-traversal entry (../../evil.txt) must be skipped, not extracted."""
        tool = _make_tool(tmp_path)
        archive = tmp_path / "evil.tar.gz"
        dest = tmp_path / "out"
        dest.mkdir()
        evil_path = "../../evil.txt"
        _create_tar_gz(archive, {evil_path: b"pwned", "safe.txt": b"ok"})
        tool._extract_archive(archive, dest, "tar.gz")
        # safe.txt must be present; evil.txt must NOT escape target dir
        assert (dest / "safe.txt").read_bytes() == b"ok"
        escaped = (tmp_path.parent / "evil.txt")
        assert not escaped.exists()


class TestExtractArchiveTarBz2:
    def test_extracts_files(self, tmp_path):
        tool = _make_tool(tmp_path)
        archive = tmp_path / "test.tar.bz2"
        dest = tmp_path / "out"
        dest.mkdir()
        _create_tar_bz2(archive, {"data.bin": b"\x00\x01\x02"})
        tool._extract_archive(archive, dest, "tar.bz2")
        assert (dest / "data.bin").read_bytes() == b"\x00\x01\x02"


class TestExtractArchiveZip:
    def test_extracts_files(self, tmp_path):
        tool = _make_tool(tmp_path)
        archive = tmp_path / "test.zip"
        dest = tmp_path / "out"
        dest.mkdir()
        _create_zip(archive, {"readme.txt": b"hello"})
        tool._extract_archive(archive, dest, "zip")
        assert (dest / "readme.txt").read_bytes() == b"hello"

    def test_zipslip_member_skipped(self, tmp_path):
        tool = _make_tool(tmp_path)
        archive = tmp_path / "evil.zip"
        dest = tmp_path / "out"
        dest.mkdir()
        _create_zip(archive, {"../../evil.txt": b"pwned", "safe.txt": b"ok"})
        tool._extract_archive(archive, dest, "zip")
        assert (dest / "safe.txt").read_bytes() == b"ok"
        assert not (tmp_path.parent / "evil.txt").exists()

    def test_unsupported_archive_type_raises(self, tmp_path):
        tool = _make_tool(tmp_path)
        with pytest.raises(ValueError, match="Unsupported archive type"):
            tool._extract_archive(tmp_path / "x.rar", tmp_path, "rar")


# ---------------------------------------------------------------------------
# Base class cache directory logic
# ---------------------------------------------------------------------------

class TestCacheableToolBaseDir:
    def test_default_base_dir_uses_home(self, tmp_path):
        from pymcu.toolchain.sdk.base_tool import CacheableTool, _default_platform_key

        class _DummyTool(CacheableTool):
            def get_name(self): return "dummy"
            def is_cached(self): return False
            def install(self): return Path()

        # Clear PYMCU_TOOLS_DIR so the default path is used
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("PYMCU_TOOLS_DIR", None)
            tool = _DummyTool(_make_console())
        assert str(Path.home()) in str(tool.base_dir)
        assert _default_platform_key() in str(tool.base_dir)

    def test_env_override_sets_base_dir(self, tmp_path):
        tool = _make_tool(tmp_path)
        assert str(tmp_path) in str(tool.base_dir)

    def test_relative_tools_dir_is_resolved_to_absolute(self, tmp_path):
        from pymcu.toolchain.sdk.base_tool import CacheableTool

        class _DummyTool(CacheableTool):
            def get_name(self): return "dummy"
            def is_cached(self): return False
            def install(self): return Path()

        # Path.resolve() turns a relative path into an absolute one, so the
        # constructor succeeds and base_dir is always absolute.
        with patch.dict(os.environ, {"PYMCU_TOOLS_DIR": "relative/path"}, clear=False):
            tool = _DummyTool(_make_console())
        assert tool.base_dir.is_absolute()


# ---------------------------------------------------------------------------
# Version file helpers
# ---------------------------------------------------------------------------

class TestVersionFile:
    def test_write_and_read_version(self, tmp_path):
        tool = _make_tool(tmp_path)
        tool._write_cached_version("1.2.3")
        assert tool._read_cached_version() == "1.2.3"

    def test_read_missing_version_returns_empty(self, tmp_path):
        tool = _make_tool(tmp_path)
        # Don't write anything
        assert tool._read_cached_version() == ""
