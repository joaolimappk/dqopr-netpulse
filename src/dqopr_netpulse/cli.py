"""Command-line interface for testable monitoring and exports."""

from __future__ import annotations

import argparse
import asyncio
import logging
from pathlib import Path

from dqopr_netpulse.configuration import DEFAULT_PUBLIC_TARGETS, AppConfig, default_data_dir
from dqopr_netpulse.exports.csv_export import export_session_csv, export_session_zip
from dqopr_netpulse.monitoring.engine import MonitoringSession
from dqopr_netpulse.reports.html_report import generate_html_report
from dqopr_netpulse.storage import NetPulseStore


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="dqopr-netpulse",
        description="Internet Quality Monitor and ISP Evidence Reporter",
    )
    parser.add_argument(
        "--data-dir", type=Path, default=default_data_dir(), help="Application data directory"
    )
    parser.add_argument("--database", type=Path, help="SQLite database path")
    parser.add_argument("--verbose", action="store_true", help="Enable debug logging")
    subparsers = parser.add_subparsers(dest="command", required=True)

    monitor = subparsers.add_parser("monitor", help="Run a monitoring session")
    monitor.add_argument("--duration-minutes", type=float, default=10.0)
    monitor.add_argument("--cycles", type=int)
    monitor.add_argument("--download-mbps", type=float)
    monitor.add_argument("--upload-mbps", type=float)
    monitor.add_argument("--speedtest", action="store_true", help="Enable optional speed testing")
    monitor.add_argument("--latency-interval", type=float, default=2.0)

    export = subparsers.add_parser("export-csv", help="Export session CSV files")
    export.add_argument("session_id")
    export.add_argument("output_dir", type=Path)

    archive = subparsers.add_parser(
        "export-zip", help="Export all session CSV files into a ZIP archive"
    )
    archive.add_argument("session_id")
    archive.add_argument("output_zip", type=Path)

    report = subparsers.add_parser("report", help="Generate a self-contained HTML ISP report")
    report.add_argument("session_id")
    report.add_argument("output_html", type=Path)
    report.add_argument("--download-mbps", type=float)
    report.add_argument("--upload-mbps", type=float)
    report.add_argument("--technical", action="store_true", help="Disable private report mode")
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    logging.basicConfig(level=logging.DEBUG if args.verbose else logging.INFO)
    database_path = args.database or Path(args.data_dir) / "netpulse.sqlite3"
    store = NetPulseStore(database_path)
    try:
        if args.command == "monitor":
            config = _monitor_config(args)
            session = MonitoringSession(config, store)
            session_id = asyncio.run(session.run())
            print(session_id)
            return 0
        if args.command == "export-csv":
            files = export_session_csv(store, args.session_id, args.output_dir)
            for path in files:
                print(path)
            return 0
        if args.command == "export-zip":
            print(export_session_zip(store, args.session_id, args.output_zip))
            return 0
        if args.command == "report":
            print(
                generate_html_report(
                    store,
                    args.session_id,
                    args.output_html,
                    contracted_download_mbps=args.download_mbps,
                    contracted_upload_mbps=args.upload_mbps,
                    private_mode=not args.technical,
                )
            )
            return 0
    finally:
        store.close()
    parser.error("Unknown command")
    return 2


def _monitor_config(args: argparse.Namespace) -> AppConfig:
    from dataclasses import replace

    base = AppConfig(data_dir=args.data_dir)
    session = replace(
        base.session,
        contracted_download_mbps=args.download_mbps,
        contracted_upload_mbps=args.upload_mbps,
        duration_seconds=int(args.duration_minutes * 60) if args.duration_minutes else None,
        cycle_count=args.cycles,
        speedtest_enabled=args.speedtest,
        latency_interval_seconds=args.latency_interval,
        targets=DEFAULT_PUBLIC_TARGETS,
    )
    return replace(base, session=session)


if __name__ == "__main__":
    raise SystemExit(main())
