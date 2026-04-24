# -----------------------------------------------------------------------------
# PyMCU Plugin SDK
# Copyright (C) 2026 Ivan Montiel Cardona and the PyMCU Project Authors
#
# SPDX-License-Identifier: MIT
# -----------------------------------------------------------------------------
# SAFETY WARNING / HIGH RISK ACTIVITIES:
# THE SOFTWARE IS NOT DESIGNED, MANUFACTURED, OR INTENDED FOR USE IN HAZARDOUS
# ENVIRONMENTS REQUIRING FAIL-SAFE PERFORMANCE, SUCH AS IN THE OPERATION OF
# NUCLEAR FACILITIES, AIRCRAFT NAVIGATION OR COMMUNICATION SYSTEMS, AIR
# TRAFFIC CONTROL, DIRECT LIFE SUPPORT MACHINES, OR WEAPONS SYSTEMS.
# -----------------------------------------------------------------------------

"""
PyPIToolchain — generic base class for toolchains distributed as binary
PyPI wheels.

Every concrete toolchain wheel (e.g. ``pymcu-avr-toolchain``) should
subclass :class:`PyPIToolchain` and define at minimum:

- :attr:`pypi_package` — the exact PyPI package name.
- :attr:`import_name`  — the importable Python package name (underscores).

The installed wheel is expected to expose a ``get_bin_dir()`` function at the
module level that returns the :class:`~pathlib.Path` to the directory
containing the toolchain executables.

Example layout inside the installed wheel::

    pymcu_avr_toolchain/
        __init__.py          # exports get_bin_dir()
        bin/
            avr-gcc
            avr-objcopy
            ...
"""

import importlib
import importlib.metadata
import subprocess
import sys
from pathlib import Path
from typing import Optional

from rich.console import Console
from rich.progress import Progress, SpinnerColumn, TextColumn
from rich.panel import Panel

from .base_tool import _is_non_interactive
from .toolchain import ExternalToolchain


class ToolchainNotInstalledError(RuntimeError):
    """
    Raised when a required PyPI toolchain is not installed and the user
    declined (or could not be prompted) to install it.

    Attributes
    ----------
    pypi_package : str
        The PyPI package name that is missing.
    """

    def __init__(self, pypi_package: str) -> None:
        self.pypi_package = pypi_package
        super().__init__(
            f"Toolchain '{pypi_package}' is not installed.\n"
            f"Install it with:  pip install {pypi_package}"
        )


class PyPIToolchain(ExternalToolchain):
    """
    Generic base class for toolchains distributed as binary PyPI wheels.

    Subclasses must define the following class attributes:

    pypi_package : str
        PyPI package name (e.g. ``"pymcu-avr-toolchain"``).
    import_name : str
        Python import name (e.g. ``"pymcu_avr_toolchain"``).
    min_version : str
        Optional minimum required version (e.g. ``"15.2.0"``).
        An empty string disables the version check.

    The installed wheel **must** expose a ``get_bin_dir()`` function at its
    top-level ``__init__``.  That function returns a :class:`~pathlib.Path`
    pointing to the directory that contains the toolchain executables.

    Cache layout
    ------------
    Unlike download-based tools, the binaries live inside the wheel's
    site-packages directory.  The ``base_dir`` from :class:`CacheableTool` is
    therefore **not** used for storage, but ``_lock_file()`` (inherited) is
    still used to serialise concurrent installs.
    """

    pypi_package: str
    import_name: str
    min_version: str = ""

    # ------------------------------------------------------------------
    # CacheableTool ABCs
    # ------------------------------------------------------------------

    def get_name(self) -> str:
        """Return the PyPI package name (used as cache subdirectory key)."""
        return self.pypi_package

    def is_cached(self) -> bool:
        """
        Return ``True`` if the package is importable and its installed version
        meets :attr:`min_version`.

        Uses :func:`importlib.metadata.version` — no subprocess required.
        """
        try:
            installed = importlib.metadata.version(self.pypi_package)
        except importlib.metadata.PackageNotFoundError:
            return False

        if self.min_version:
            from packaging.version import Version  # type: ignore[import-untyped]
            try:
                if Version(installed) < Version(self.min_version):
                    return False
            except Exception:
                pass

        # Verify the wheel exposes get_bin_dir() so we know it is functional.
        try:
            mod = importlib.import_module(self.import_name)
            get_bin_dir = getattr(mod, "get_bin_dir", None)
            if get_bin_dir is None:
                return False
            bin_dir: Path = get_bin_dir()
            return bin_dir.is_dir()
        except Exception:
            return False

    def get_bin_dir(self) -> Path:
        """
        Return the :class:`~pathlib.Path` to the toolchain's ``bin/`` directory.

        If the package is not yet installed, ``_prompt_and_maybe_install()`` is
        called first (which may raise :class:`ToolchainNotInstalledError`).
        """
        if not self.is_cached():
            self._prompt_and_maybe_install()

        mod = importlib.import_module(self.import_name)
        return mod.get_bin_dir()

    def get_tool(self, name: str) -> Path:
        """
        Return the :class:`~pathlib.Path` to the named executable inside
        ``get_bin_dir()``.  Appends ``.exe`` on Windows automatically.
        """
        if sys.platform == "win32" and not name.endswith(".exe"):
            name = name + ".exe"
        return self.get_bin_dir() / name

    def install(self) -> Path:
        """
        Install the PyPI wheel via ``pip`` in the current Python environment.

        Shows a spinner while pip runs.  Raises :class:`RuntimeError` if pip
        exits with a non-zero status.

        Returns
        -------
        Path
            The ``bin/`` directory of the freshly installed toolchain.
        """
        package_spec = self.pypi_package
        if self.min_version:
            package_spec = f"{self.pypi_package}>={self.min_version}"

        cmd = [sys.executable, "-m", "pip", "install", package_spec]

        with Progress(
            SpinnerColumn(),
            TextColumn("[progress.description]{task.description}"),
            transient=True,
            console=self.console,
        ) as progress:
            progress.add_task(
                f"Installing [bold cyan]{self.pypi_package}[/bold cyan] via pip…",
                total=None,
            )
            result = subprocess.run(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )

        if result.returncode != 0:
            stderr = result.stderr.decode(errors="replace").strip()
            raise RuntimeError(
                f"pip install {self.pypi_package!r} failed "
                f"(exit {result.returncode}):\n{stderr}"
            )

        # Invalidate importlib cache so the newly installed package is visible.
        importlib.invalidate_caches()

        mod = importlib.import_module(self.import_name)
        self.console.print(
            f"[green]✓[/green] Installed [bold cyan]{self.pypi_package}[/bold cyan]"
        )
        return mod.get_bin_dir()

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _prompt_and_maybe_install(self) -> None:
        """
        Prompt the user (or auto-accept in CI) to install the missing toolchain.

        In non-interactive mode (CI / piped stdin / ``PYMCU_NO_INTERACTIVE=1``),
        the install proceeds automatically without a prompt.

        Raises
        ------
        ToolchainNotInstalledError
            If the user declines installation.
        """
        install_cmd = self.pypi_package
        if self.min_version:
            install_cmd = f"{self.pypi_package}>={self.min_version}"

        message = (
            f"[bold yellow]⚠  Toolchain not found:[/bold yellow] "
            f"[bold cyan]{self.pypi_package}[/bold cyan]\n\n"
            f"This toolchain is required to compile for "
            f"[bold]{self.chip or 'the target'}[/bold] targets.\n\n"
            f"Install with:  [bold]pip install {install_cmd}[/bold]"
        )

        if _is_non_interactive():
            self.console.print(
                Panel(
                    message + "\n\n[dim]Non-interactive mode: installing automatically.[/dim]",
                    title="PyMCU Toolchain",
                    border_style="yellow",
                )
            )
            self.install()
            return

        self.console.print(
            Panel(
                message,
                title="PyMCU Toolchain",
                border_style="yellow",
            )
        )

        try:
            answer = input("Install now? [y/N] ").strip().lower()
        except (EOFError, KeyboardInterrupt):
            answer = "n"

        if answer in ("y", "yes"):
            self.install()
        else:
            raise ToolchainNotInstalledError(self.pypi_package)

    # ------------------------------------------------------------------
    # ExternalToolchain ABC stubs — subclasses must implement assemble()
    # ------------------------------------------------------------------

    @classmethod
    def supports(cls, chip: str) -> bool:  # pragma: no cover
        """Subclasses must override to declare supported chips."""
        raise NotImplementedError(
            f"{cls.__name__}.supports() is not implemented"
        )

    def assemble(
        self, asm_file: Path, output_file: Optional[Path] = None
    ) -> Path:  # pragma: no cover
        """Subclasses must override to implement assembly."""
        raise NotImplementedError(
            f"{type(self).__name__}.assemble() is not implemented"
        )
