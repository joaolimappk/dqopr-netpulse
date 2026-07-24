#!/usr/bin/env python3
"""Release validation helper for DQOPR NetPulse."""

from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import importlib.util
import json
import os
import platform
import re
import shutil
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from datetime import UTC, datetime
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DIST_DIR = REPO_ROOT / "dist"
ARTIFACT_DIR = REPO_ROOT / "release_artifacts"

REQUIRED_FILES = (
    "README.md",
    "LICENSE",
    "NOTICE",
    "CONTRIBUTING.md",
    "CODE_OF_CONDUCT.md",
    "SECURITY.md",
    "CHANGELOG.md",
    "CONTRIBUTORS.md",
    "THIRD_PARTY_NOTICES.md",
    "PRIVACY.md",
    "docs/architecture.md",
    "docs/methodology.md",
    "docs/privacy.md",
    "docs/packaging.md",
    "docs/signing.md",
    "docs/release.md",
    "packaging/windows/netpulse.iss",
    ".github/workflows/windows-build-test.yml",
    ".github/ISSUE_TEMPLATE/bug_report.md",
    ".github/ISSUE_TEMPLATE/feature_request.md",
    ".github/pull_request_template.md",
)

SECRET_PATTERNS = (
    ("private key block", re.compile(r"-----BEGIN [A-Z ]*PRIVATE KEY-----")),
    ("AWS access key", re.compile(r"\bAKIA[0-9A-Z]{16}\b")),
    ("GitHub token", re.compile(r"\bgh[pousr]_[A-Za-z0-9_]{30,}\b")),
    ("OpenAI style API key", re.compile(r"\bsk-[A-Za-z0-9]{32,}\b")),
    (
        "long secret assignment",
        re.compile(
            r"(?i)\b(api[_-]?key|client[_-]?secret|password|secret|token)\b"
            r"\s*[:=]\s*['\"]?[A-Za-z0-9_./+=-]{24,}"
        ),
    ),
)

TEXT_SUFFIXES = {
    ".cfg",
    ".ini",
    ".iss",
    ".json",
    ".md",
    ".py",
    ".toml",
    ".txt",
    ".yaml",
    ".yml",
}


@dataclass
class CheckResult:
    name: str
    status: str
    detail: str = ""


@dataclass
class ValidationReport:
    started_at_utc: str
    finished_at_utc: str | None = None
    version: str = "unknown"
    platform: str = field(default_factory=platform.platform)
    python: str = field(default_factory=lambda: sys.version)
    results: list[CheckResult] = field(default_factory=list)
    artifacts: list[str] = field(default_factory=list)

    def add(self, name: str, status: str, detail: str = "") -> None:
        self.results.append(CheckResult(name=name, status=status, detail=detail))

    @property
    def failed(self) -> bool:
        return any(result.status == "fail" for result in self.results)

    @property
    def warned(self) -> bool:
        return any(result.status == "warn" for result in self.results)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate DQOPR NetPulse release readiness.")
    parser.add_argument("--strict", action="store_true", help="Treat warnings as release failures.")
    parser.add_argument(
        "--skip-builds", action="store_true", help="Skip executable and installer builds."
    )
    parser.add_argument(
        "--signing-expected",
        action="store_true",
        help="Fail when executable or installer artifacts are unsigned.",
    )
    parser.add_argument("--artifact-dir", type=Path, default=ARTIFACT_DIR)
    args = parser.parse_args()

    report = ValidationReport(started_at_utc=utc_timestamp())
    report.version = project_version()
    args.artifact_dir.mkdir(parents=True, exist_ok=True)

    check_required_files(report)
    run_quality_checks(report)
    scan_for_secrets(report)

    if args.skip_builds:
        report.add("builds", "skip", "Skipped by --skip-builds.")
    else:
        build_executable(report)
        build_installer(report)

    artifact_paths = collect_artifacts(args.artifact_dir)
    write_checksums(report, artifact_paths, args.artifact_dir)
    verify_signatures(report, artifact_paths, signing_expected=args.signing_expected)
    write_metadata(report, args.artifact_dir)

    print_summary(report)
    if report.failed or (args.strict and report.warned):
        return 1
    return 0


def utc_timestamp() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat()


def project_version() -> str:
    try:
        return importlib.metadata.version("dqopr-netpulse")
    except importlib.metadata.PackageNotFoundError:
        pyproject = REPO_ROOT / "pyproject.toml"
        match = re.search(r'^version\s*=\s*"([^"]+)"', pyproject.read_text(encoding="utf-8"), re.M)
        return match.group(1) if match else "unknown"


def check_required_files(report: ValidationReport) -> None:
    missing = [path for path in REQUIRED_FILES if not (REPO_ROOT / path).is_file()]
    if missing:
        report.add("required files", "fail", "Missing: " + ", ".join(missing))
    else:
        report.add("required files", "pass", f"{len(REQUIRED_FILES)} files present.")


def run_quality_checks(report: ValidationReport) -> None:
    if module_available("ruff"):
        run_command(report, "ruff", [sys.executable, "-m", "ruff", "check", "."])
    else:
        report.add("ruff", "warn", "ruff is not installed.")

    if module_available("mypy"):
        run_command(report, "mypy", [sys.executable, "-m", "mypy", "src/dqopr_netpulse"])
    else:
        report.add("mypy", "warn", "mypy is not installed.")

    if module_available("pytest"):
        pytest_result = run_command(
            report,
            "pytest",
            [sys.executable, "-m", "pytest"],
            allow_no_tests=True,
        )
    else:
        pytest_result = 127
        report.add("pytest", "warn", "pytest is not installed.")

    if pytest_result == 5:
        report.add(
            "pytest coverage", "warn", "No tests were collected; add tests before strict release."
        )


def module_available(name: str) -> bool:
    return importlib.util.find_spec(name) is not None


def run_command(
    report: ValidationReport,
    name: str,
    command: list[str],
    *,
    allow_no_tests: bool = False,
) -> int:
    try:
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
    except FileNotFoundError as exc:
        report.add(name, "warn", f"Tool unavailable: {exc}")
        return 127

    output = completed.stdout.strip()
    if completed.returncode == 0:
        report.add(name, "pass", last_lines(output))
    elif allow_no_tests and completed.returncode == 5:
        report.add(name, "warn", last_lines(output) or "No tests collected.")
    else:
        report.add(name, "fail", last_lines(output))
    return int(completed.returncode)


def last_lines(text: str, limit: int = 12) -> str:
    lines = text.splitlines()
    return "\n".join(lines[-limit:])


def scan_for_secrets(report: ValidationReport) -> None:
    findings: list[str] = []
    for path in iter_text_files(REPO_ROOT):
        rel = path.relative_to(REPO_ROOT).as_posix()
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        for label, pattern in SECRET_PATTERNS:
            for match in pattern.finditer(text):
                line_no = text.count("\n", 0, match.start()) + 1
                findings.append(f"{rel}:{line_no}: {label}")

    if findings:
        report.add("secret scan", "fail", "\n".join(findings[:50]))
    else:
        report.add("secret scan", "pass", "No common high-confidence secret patterns found.")


def iter_text_files(root: Path) -> list[Path]:
    ignored_dirs = {
        ".git",
        ".mypy_cache",
        ".pytest_cache",
        ".ruff_cache",
        ".venv",
        "__pycache__",
        "build",
        "dist",
        "release_artifacts",
    }
    files: list[Path] = []
    for current_root, dirnames, filenames in os.walk(root):
        dirnames[:] = [name for name in dirnames if name not in ignored_dirs]
        for filename in filenames:
            path = Path(current_root) / filename
            if path.suffix.lower() in TEXT_SUFFIXES or path.name in {"LICENSE"}:
                files.append(path)
    return files


def build_executable(report: ValidationReport) -> None:
    pyinstaller = shutil.which("pyinstaller")
    gui_path = REPO_ROOT / "src" / "dqopr_netpulse" / "gui" / "app.py"
    if pyinstaller is None:
        report.add("pyinstaller build", "warn", "PyInstaller is not installed.")
        return
    if not gui_path.is_file():
        report.add(
            "pyinstaller build",
            "fail",
            "Missing GUI entry point: src/dqopr_netpulse/gui/app.py.",
        )
        return

    command = [
        pyinstaller,
        "--name",
        "DQOPR-NetPulse",
        "--noconfirm",
        "--clean",
        "--windowed",
        "--noupx",
        "--paths",
        str(REPO_ROOT / "src"),
        str(gui_path),
    ]
    result = run_command(report, "pyinstaller build", command)
    executable = DIST_DIR / "DQOPR-NetPulse" / "DQOPR-NetPulse.exe"
    if result == 0 and executable.is_file():
        report.add("pyinstaller output", "pass", f"Found {executable.relative_to(REPO_ROOT)}.")
    elif result == 0:
        report.add(
            "pyinstaller output",
            "fail",
            f"PyInstaller finished but did not create {executable.relative_to(REPO_ROOT)}.",
        )


def build_installer(report: ValidationReport) -> None:
    iscc = shutil.which("iscc") or shutil.which("ISCC.exe")
    source_dir = DIST_DIR / "DQOPR-NetPulse"
    if iscc is None:
        report.add("installer build", "warn", "Inno Setup compiler is not installed.")
        return
    if not source_dir.is_dir():
        report.add("installer build", "warn", f"Missing PyInstaller output: {source_dir}")
        return

    env = os.environ.copy()
    env["DQOPR_NETPULSE_VERSION"] = project_version()
    completed = subprocess.run(
        [iscc, str(REPO_ROOT / "packaging" / "windows" / "netpulse.iss")],
        cwd=REPO_ROOT,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )
    if completed.returncode == 0:
        report.add("installer build", "pass", last_lines(completed.stdout))
        installers = find_installer_artifacts()
        if installers:
            report.add(
                "installer output",
                "pass",
                "\n".join(path.relative_to(REPO_ROOT).as_posix() for path in installers),
            )
        else:
            report.add(
                "installer output",
                "fail",
                "Inno Setup completed but no DQOPR-NetPulse-Setup-*.exe was created.",
            )
    else:
        report.add("installer build", "fail", last_lines(completed.stdout))


def find_installer_artifacts() -> list[Path]:
    return sorted((REPO_ROOT / "release_artifacts").glob("DQOPR-NetPulse-Setup-*.exe"))


def collect_artifacts(artifact_dir: Path) -> list[Path]:
    candidates: list[Path] = []
    for root in (artifact_dir, DIST_DIR):
        if root.exists():
            for path in root.rglob("*"):
                if path.is_file() and path.name not in {"SHA256SUMS.txt", "release_metadata.json"}:
                    candidates.append(path)
    return sorted(set(candidates))


def write_checksums(report: ValidationReport, artifacts: list[Path], artifact_dir: Path) -> None:
    if not artifacts:
        report.add("checksums", "warn", "No release artifacts found.")
        return

    checksum_path = artifact_dir / "SHA256SUMS.txt"
    lines = []
    for path in artifacts:
        digest = sha256(path)
        rel = path.relative_to(REPO_ROOT).as_posix()
        lines.append(f"{digest}  {rel}")
        report.artifacts.append(rel)
    checksum_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    report.add("checksums", "pass", f"Wrote {checksum_path.relative_to(REPO_ROOT)}.")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_signatures(
    report: ValidationReport,
    artifacts: list[Path],
    *,
    signing_expected: bool,
) -> None:
    signable = [path for path in artifacts if path.suffix.lower() in {".exe", ".dll", ".msi"}]
    if not signable:
        status = "fail" if signing_expected else "skip"
        report.add("signature verification", status, "No signable artifacts found.")
        return

    if platform.system() != "Windows" or shutil.which("powershell") is None:
        status = "fail" if signing_expected else "warn"
        report.add(
            "signature verification",
            status,
            "Authenticode verification requires Windows PowerShell.",
        )
        return

    unsigned: list[str] = []
    for path in signable:
        command = [
            "powershell",
            "-NoProfile",
            "-Command",
            f"(Get-AuthenticodeSignature -LiteralPath '{path}').Status",
        ]
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        if completed.stdout.strip() != "Valid":
            unsigned.append(f"{path.relative_to(REPO_ROOT).as_posix()}: {completed.stdout.strip()}")

    if unsigned:
        status = "fail" if signing_expected else "warn"
        report.add("signature verification", status, "\n".join(unsigned))
    else:
        report.add("signature verification", "pass", f"{len(signable)} signed artifact(s) valid.")


def write_metadata(report: ValidationReport, artifact_dir: Path) -> None:
    report.finished_at_utc = utc_timestamp()
    metadata_path = artifact_dir / "release_metadata.json"
    report.add("metadata", "pass", f"Wrote {metadata_path.relative_to(REPO_ROOT)}.")
    metadata_path.write_text(
        json.dumps(asdict(report), indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def print_summary(report: ValidationReport) -> None:
    for result in report.results:
        print(f"[{result.status.upper()}] {result.name}")
        if result.detail:
            print(result.detail)
    print(f"Version: {report.version}")
    print(f"Artifacts: {len(report.artifacts)}")


if __name__ == "__main__":
    raise SystemExit(main())
